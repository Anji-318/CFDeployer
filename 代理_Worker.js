export default {
  async fetch(request, env) {
    // ========== CORS 预检处理 ==========
    if (request.method === 'OPTIONS') {
      return new Response(null, {
        headers: {
          'Access-Control-Allow-Origin': '*',
          'Access-Control-Allow-Methods': 'PUT, POST, DELETE, OPTIONS',
          'Access-Control-Allow-Headers': 'Authorization, Content-Type',
        }
      });
    }

    // ========== 配置 ==========
    const DEFAULT_CONFIG = {
      DEPLOY_KEY: '',
      ALLOWED_ORIGINS: '*',
      MAX_BODY_SIZE: 50 * 1024 * 1024,
    };

    const CONFIG = {
      DEPLOY_KEY: env.DEPLOY_KEY || DEFAULT_CONFIG.DEPLOY_KEY,
      ALLOWED_ORIGINS: env.ALLOWED_ORIGINS || DEFAULT_CONFIG.ALLOWED_ORIGINS,
      MAX_BODY_SIZE: env.MAX_BODY_SIZE ? parseInt(env.MAX_BODY_SIZE) : DEFAULT_CONFIG.MAX_BODY_SIZE,
    };

    // ========== 密钥验证 ==========
    const authHeader = request.headers.get('Authorization');
    const providedKey = authHeader?.startsWith('Bearer ') ? authHeader.slice(7) : null;
    
    if (CONFIG.DEPLOY_KEY && providedKey !== CONFIG.DEPLOY_KEY) {
      return new Response(JSON.stringify({ 
        success: false,
        error: 'Unauthorized: Invalid or missing DEPLOY_KEY' 
      }), {
        status: 401,
        headers: { 
          'Content-Type': 'application/json',
          'Access-Control-Allow-Origin': '*'
        }
      });
    }

    // ========== 请求处理 ==========
    try {
      const url = new URL(request.url);
      const path = url.pathname;

      if (path === '/deploy/single' && request.method === 'POST') {
        return await handleSingleDeploy(request, CONFIG);
      } else if (path === '/deploy/batch' && request.method === 'POST') {
        return await handleBatchDeploy(request, CONFIG);
      } else if (path === '/health' && request.method === 'GET') {
        return await handleHealthCheck(CONFIG);
      } else {
        return new Response(JSON.stringify({ 
          success: false,
          error: 'Not Found',
          availableEndpoints: ['/deploy/single', '/deploy/batch', '/health']
        }), {
          status: 404,
          headers: { 
            'Content-Type': 'application/json',
            'Access-Control-Allow-Origin': '*'
          }
        });
      }
    } catch (error) {
      console.error('Proxy Error:', error);
      return new Response(JSON.stringify({ 
        success: false,
        error: error.message,
        stack: error.stack
      }), {
        status: 500,
        headers: { 
          'Content-Type': 'application/json',
          'Access-Control-Allow-Origin': '*'
        }
      });
    }
  }
};

function detectScriptType(script) {
  const hasImport = /^\s*import\s+/m.test(script);
  const hasExportDefault = /^\s*export\s+default/m.test(script);
  if (hasImport || hasExportDefault) return 'module';
  return 'service-worker';
}

async function handleSingleDeploy(request, CONFIG) {
  const contentLength = request.headers.get('Content-Length');
  if (contentLength && parseInt(contentLength) > CONFIG.MAX_BODY_SIZE) {
    throw new Error(`Request body too large. Max: ${CONFIG.MAX_BODY_SIZE} bytes`);
  }

  let body;
  try {
    body = await request.json();
  } catch (e) {
    throw new Error(`Failed to parse JSON: ${e.message}`);
  }

  const { 
    accountId, 
    apiToken, 
    workerName, 
    script, 
    secrets = {}, 
    routes = [], 
    subdomain = false,
    kvNamespaces = []
  } = body;

  if (!accountId || !apiToken || !workerName || !script) {
    const missing = [];
    if (!accountId) missing.push('accountId');
    if (!apiToken) missing.push('apiToken');
    if (!workerName) missing.push('workerName');
    if (!script) missing.push('script');
    throw new Error(`Missing required fields: ${missing.join(', ')}`);
  }

  const scriptType = detectScriptType(script);
  const scriptSize = new Blob([script]).size;
  console.log(`[Deploy] Worker: ${workerName}, Type: ${scriptType}, Size: ${scriptSize} bytes`);

  const results = {
    success: true,
    workerName,
    scriptType,
    scriptSize,
    steps: []
  };

  let deployRes;
  let retryCount = 0;
  const maxRetries = 2;

  while (retryCount <= maxRetries) {
    try {
      if (scriptType === 'module') {
        const formData = new FormData();
        const bindings = [];
        
        for (const [key, value] of Object.entries(secrets)) {
          bindings.push({
            type: 'secret_text',
            name: key,
            text: value
          });
        }

        for (const kv of kvNamespaces) {
          bindings.push({
            type: 'kv_namespace',
            name: kv.name,
            namespace_id: kv.id
          });
        }

        const metadata = {
          main_module: 'index.js',
          bindings: bindings
        };

        formData.append('metadata', JSON.stringify(metadata));
        formData.append('index.js', new Blob([script], { type: 'application/javascript+module' }));

        // ✅ 修复：无空格的URL
        deployRes = await fetch(
          `https://api.cloudflare.com/client/v4/accounts/${accountId}/workers/scripts/${workerName}`,
          {
            method: 'PUT',
            headers: {
              'Authorization': `Bearer ${apiToken}`
            },
            body: formData
          }
        );

      } else {
        // ✅ 修复：无空格的URL
        deployRes = await fetch(
          `https://api.cloudflare.com/client/v4/accounts/${accountId}/workers/scripts/${workerName}`,
          {
            method: 'PUT',
            headers: {
              'Authorization': `Bearer ${apiToken}`,
              'Content-Type': 'application/javascript'
            },
            body: script
          }
        );
      }

      if (!deployRes.ok) {
        const errorData = await deployRes.json().catch(() => ({}));
        throw new Error(`Deploy failed: ${errorData.errors?.[0]?.message || deployRes.statusText}`);
      }

      break;

    } catch (error) {
      retryCount++;
      console.error(`[Deploy] Attempt ${retryCount} failed:`, error.message);
      
      if (retryCount > maxRetries) {
        throw new Error(`Deploy failed after ${maxRetries + 1} attempts: ${error.message}`);
      }
      
      await new Promise(resolve => setTimeout(resolve, 1000 * retryCount));
    }
  }

  const deployData = await deployRes.json();
  results.steps.push({ step: 'script', status: 'success', format: scriptType, retries: retryCount });

  // ✅ 修复：无空格的URL（Secrets）
  if (scriptType === 'service-worker' && Object.keys(secrets).length > 0) {
    try {
      const secretPromises = Object.entries(secrets).map(async ([key, value]) => {
        const res = await fetch(
          `https://api.cloudflare.com/client/v4/accounts/${accountId}/workers/scripts/${workerName}/secrets`,
          {
            method: 'PUT',
            headers: {
              'Authorization': `Bearer ${apiToken}`,
              'Content-Type': 'application/json'
            },
            body: JSON.stringify({ 
              name: key, 
              text: value, 
              type: 'secret_text' 
            })
          }
        );
        if (!res.ok) {
          const err = await res.json();
          throw new Error(`Secret ${key}: ${err.errors?.[0]?.message || 'Failed'}`);
        }
        return key;
      });

      const secretKeys = await Promise.all(secretPromises);
      results.steps.push({ step: 'secrets', status: 'success', keys: secretKeys });
    } catch (error) {
      results.steps.push({ step: 'secrets', status: 'warning', error: error.message });
    }
  }

  // ✅ 修复：无空格的URL（Routes）
  if (routes.length > 0) {
    try {
      const routeRes = await fetch(
        `https://api.cloudflare.com/client/v4/accounts/${accountId}/workers/scripts/${workerName}/routes`,
        {
          method: 'PUT',
          headers: {
            'Authorization': `Bearer ${apiToken}`,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify(routes.map(r => ({ 
            pattern: r.pattern, 
            zone_id: r.zone_id 
          })))
        }
      );
      if (!routeRes.ok) {
        const err = await routeRes.json();
        throw new Error(err.errors?.[0]?.message || 'Route setup failed');
      }
      results.steps.push({ step: 'routes', status: 'success', count: routes.length });
    } catch (error) {
      results.steps.push({ step: 'routes', status: 'warning', error: error.message });
    }
  }

  // ✅ 修复：无空格的URL（Subdomain）
  if (subdomain) {
    try {
      const subRes = await fetch(
        `https://api.cloudflare.com/client/v4/accounts/${accountId}/workers/scripts/${workerName}/subdomain`,
        {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${apiToken}`,
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({ enabled: true })
        }
      );
      if (!subRes.ok) {
        const err = await subRes.json();
        results.steps.push({ step: 'subdomain', status: 'warning', message: err.errors?.[0]?.message });
      } else {
        results.steps.push({ step: 'subdomain', status: 'success' });
      }
    } catch (error) {
      results.steps.push({ step: 'subdomain', status: 'warning', error: error.message });
    }
  }

  return new Response(JSON.stringify(results), {
    headers: { 
      'Content-Type': 'application/json',
      'Access-Control-Allow-Origin': '*'
    }
  });
}

async function handleBatchDeploy(request, CONFIG) {
  const { jobs = [], concurrency = 2 } = await request.json();
  
  if (!Array.isArray(jobs) || jobs.length === 0) {
    throw new Error('Invalid or empty jobs array');
  }

  const results = [];
  let index = 0;

  const runNext = async () => {
    if (index >= jobs.length) return;
    const currentIndex = index++;
    const job = jobs[currentIndex];

    try {
      // 复用单部署逻辑
      const mockRequest = {
        json: async () => job
      };
      const response = await handleSingleDeploy(mockRequest, CONFIG);
      const result = await response.json();
      results[currentIndex] = { 
        index: currentIndex, 
        workerName: job.workerName,
        success: true, 
        result 
      };
    } catch (error) {
      results[currentIndex] = { 
        index: currentIndex, 
        workerName: job.workerName,
        success: false, 
        error: error.message 
      };
    }

    await runNext();
  };

  const workers = Array(Math.min(concurrency, jobs.length)).fill().map(runNext);
  await Promise.all(workers);

  const successCount = results.filter(r => r.success).length;

  return new Response(JSON.stringify({
    success: successCount === jobs.length,
    total: jobs.length,
    successCount,
    failedCount: jobs.length - successCount,
    results
  }), {
    headers: { 
      'Content-Type': 'application/json',
      'Access-Control-Allow-Origin': '*'
    }
  });
}

async function handleHealthCheck(CONFIG) {
  return new Response(JSON.stringify({
    status: 'ok',
    timestamp: new Date().toISOString(),
    config: {
      deployKeyConfigured: !!CONFIG.DEPLOY_KEY,
      allowedOrigins: CONFIG.ALLOWED_ORIGINS,
      maxBodySize: CONFIG.MAX_BODY_SIZE
    }
  }), {
    headers: { 
      'Content-Type': 'application/json',
      'Access-Control-Allow-Origin': '*'
    }
  });
}