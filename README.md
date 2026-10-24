# 小马播放器＋录屏

Windows 本地媒体查看、序列帧播放与录屏工具，源码版本 0.6.23。包含 WebView2 原生宿主和现有 Electron 开发入口。支持视频、图片、序列帧、文档和模型预览，以及选区录制流程。

## 开发与验证

安装 Node.js 22+，执行 `npm install`（或按清单安装 pnpm 依赖）。Windows 的构建还需要 .NET Framework 4.8 开发环境。

```powershell
npm test
npm run build
npm run start:legacy
```

本次独立源码副本的 8 项核心回归与前端/辅助程序构建通过；构建修复了 UpdateHelper 缺少 UpdateResources.cs 编译输入的问题。验证复用了本机已安装的依赖，不代表全新电脑、全部 UI 或全部录屏设备测试通过。运行应用可能打开窗口；录屏由用户主动启动。

## 原生打包

`npm run package` 使用 scripts/package-native.cjs，需要自行在 `.runtime/native-sdk` 准备 WebView2 SDK、BouncyCastle、ScreenRecorderLib、NAudio、7-Zip 和 TinyCC。源码保留现有打包布局；脚本内 .NET SDK 编译器路径须按本机版本调整。本次未重新完成原生安装包构建。此仓库是源码发布，不包含已签名成品。

图标重新生成使用 sharp。第三方 JavaScript、FFmpeg、WebView2 与音频/录屏库按各自许可证使用；项目自制源码使用 MIT。依赖由包管理器或其官方发行渠道取得，不把它们视为本项目原创。打包脚本会复制依赖许可证，分发前仍需核对所选二进制版本的完整许可。

公开副本不包含私钥、部署脚本、运行配置、用户素材或录屏文件。更新验签用公开配置和公开密钥保留，未提供发布者签名私钥。
