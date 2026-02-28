📋 项目概述
CFDeployer 是一款基于 WPF (.NET) 开发的桌面应用程序，专为简化 Cloudflare Workers 的部署流程而设计。
该工具提供可视化界面，支持多账号管理、代码编辑、批量部署和日志追踪等功能，帮助开发者高效管理无服务器函数部署。
| 特性           | 说明                                              |
| :----------- | :---------------------------------------------- |
| 🔧 独立配置管理    | 支持多配置文件，每个配置独立管理 Account ID、API Token、Worker 名称 |
| 👥 账户组管理     | 将多个账户分组，便于批量操作和权限管理                             |
| 📋 Worker 模板 | 支持变量化模板，一键生成多环境、多地域 Worker                      |
| 🚀 矩阵部署      | 基于模板变量组合，批量生成并部署多个 Worker 实例                    |
| 🌐 代理中转      | 内置代理检测和配置，解决网络访问问题                              |
| 🎨 主题切换      | 支持深色/浅色主题，适配不同使用环境                              |
项目结构
CFDeployer/
├── CFDeployer.csproj              # 项目文件
├── app.ico                        # 应用程序图标
├── App.xaml / App.xaml.cs         # 应用程序入口
├── MainWindow.xaml / .cs          # 主窗口
├── MainViewModel.cs               # MVVM 视图模型
├── app.manifest                   # 应用程序清单
├── Models/                        # 数据模型层
│   ├── Profile.cs                 # 部署配置档案
│   ├── DeployJob.cs               # 部署任务
│   ├── AccountGroup.cs            # 账号分组
│   ├── AppData.cs                 # 应用数据
│   ├── WorkerTemplate.cs          # Worker 模板
│   └── LogEntry.cs                # 日志条目
├── Controls/                      # 自定义控件
│   └── CodeEditor.xaml / .cs      # 代码编辑器控件
├── Dialogs/                       # 对话框
│   └── ProxyConfigDialog.xaml / .cs   # 代理配置对话框
└── Services/                      # 业务服务层
    ├── DeployService.cs           # 部署服务
    └── StorageService.cs          # 存储服务
