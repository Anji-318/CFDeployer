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
```
CFDeployer/
├── CFDeployer.csproj              # 项目文件
├── app.ico                        # 应用程序图标
├── App.xaml                       # 应用资源字典
├── App.xaml.cs                    # 应用启动逻辑
├── MainWindow.xaml                # 主窗口界面
├── MainWindow.xaml.cs             # 主窗口逻辑
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
│   ├── CodeEditor.xaml.cs         # 代码编辑器后台
│   └── CodeEditor.xaml            # 代码编辑器界面
├── Dialogs/                       # 对话框
│   ├── ProxyConfigDialog.xaml     # 代理配置界面
│   └── ProxyConfigDialog.xaml.cs  # 代理配置逻辑
└── Services/                      # 业务服务层
    ├── DeployService.cs           # 部署服务
    └── StorageService.cs          # 存储服务
```   
<img width="983" height="888" alt="图片" src="https://github.com/user-attachments/assets/53e4e543-f2ad-4b01-95b4-5da50adee943" />
<img width="983" height="890" alt="图片" src="https://github.com/user-attachments/assets/35b269dc-d35a-4d0f-a4e2-1adf07dd0aed" />
<img width="985" height="890" alt="图片" src="https://github.com/user-attachments/assets/91b45759-f780-4a29-a9f2-d386ae677dc7" />
<img width="987" height="890" alt="图片" src="https://github.com/user-attachments/assets/82d80f04-5363-4a9b-8cff-be04b2abe3d4" />
<img width="985" height="892" alt="图片" src="https://github.com/user-attachments/assets/1df42aae-a41b-4072-ba14-a5cf161c9500" />




