using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CFDeployer.Services
{
    /// <summary>
    /// Worker 代码处理服务 - 编码/解码/混淆/格式化
    /// </summary>
    public class WorkerCodeProcessor
    {
        #region 解码方法

        public string DecodeBase64(string input)
        {
            try
            {
                return Regex.Replace(input, @"atob\s*\(\s*['""]([A-Za-z0-9+/=]+)['""]\s*\)", m =>
                {
                    var base64 = m.Groups[1].Value;
                    return $"'{Encoding.UTF8.GetString(Convert.FromBase64String(base64))}'";
                });
            }
            catch { return input; }
        }

        public string DecodeUnicode(string input)
        {
            return Regex.Replace(input, @"\\u([0-9a-fA-F]{4})", 
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        }

        public string DecodeHex(string input)
        {
            var matches = Regex.Matches(input, @"\\x([0-9a-fA-F]{2})");
            if (matches.Count == 0) return input;
            
            var bytes = matches.Cast<Match>()
                .Select(m => Convert.ToByte(m.Groups[1].Value, 16))
                .ToArray();
            return Encoding.UTF8.GetString(bytes);
        }

        public string Deobfuscate(string code)
        {
            var result = code;
            result = DecodeBase64(result);
            result = DecodeHex(result);
            result = DecodeUnicode(result);
            result = Regex.Replace(result, @"'([^']*)'\s*\+\s*'([^']*)'", "'$1$2'");
            return result;
        }

        #endregion

        #region 编码/混淆方法

        public string EncodeBase64(string input)
        {
            return $"btoa('{Convert.ToBase64String(Encoding.UTF8.GetBytes(input))}')";
        }

        public string ObfuscateLight(string code)
        {
            return Regex.Replace(code, @"'([^']{10,})'", m => EncodeBase64(m.Groups[1].Value));
        }

        public string ObfuscateMedium(string code)
        {
            var result = ObfuscateLight(code);
            var counter = 0;
            var varMap = new Dictionary<string, string>();
            
            result = Regex.Replace(result, @"\blet\s+([a-zA-Z_][a-zA-Z0-9_]*)\b", m =>
            {
                var oldName = m.Groups[1].Value;
                if (!varMap.ContainsKey(oldName))
                    varMap[oldName] = $"_0x{counter++:X4}";
                return $"let {varMap[oldName]}";
            });

            foreach (var pair in varMap)
                result = Regex.Replace(result, $@"\b{pair.Key}\b", pair.Value);

            return result;
        }

        #endregion

        #region 格式化方法

        public string Format(string code)
        {
            var lines = code.Split('\n');
            var result = new StringBuilder();
            var indent = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("}") || trimmed.StartsWith("]"))
                    indent = Math.Max(0, indent - 1);

                if (!string.IsNullOrEmpty(trimmed))
                    result.AppendLine(new string(' ', indent * 2) + trimmed);
                else
                    result.AppendLine();

                if (trimmed.EndsWith("{") || trimmed.EndsWith("["))
                    indent++;
            }
            return result.ToString();
        }

        public string Minify(string code)
        {
            code = Regex.Replace(code, @"//.*$", "", RegexOptions.Multiline);
            code = Regex.Replace(code, @"/\*[\s\S]*?\*/", "");
            code = Regex.Replace(code, @"\s+", " ");
            return code.Trim();
        }

        #endregion
    }
}