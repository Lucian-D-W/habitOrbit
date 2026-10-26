# 星以为常 habitOrbit 第三方依赖

锁文件记录实际版本。`scripts/collect-licenses.ps1` 生成 `licenses/dependency-inventory.json` 和 `licenses/THIRD_PARTY_NOTICES.txt`，从包内许可及已保存的上游材料收集正文；项数以当次生成的清单为准。清单也保留构建与其他平台依赖，不表示每项都被打进 Windows 程序。缺少正文或映射文件时脚本报错；包内未附正文的来源保存在 [licenses/package-sources.json](licenses/package-sources.json)。

- .NET / Microsoft.Data.Sqlite：微软与.NET Foundation，MIT；SQLite为公有领域。NuGet依赖中的SQLitePCLRaw也需保留其许可证。
- Windows App SDK / Win2D：微软，按NuGet包LICENSE与ThirdPartyNotices保留；[Win2D包许可证入口](https://www.nuget.org/packages/Microsoft.Graphics.Win2D/1.4.0)。
- Tauri：MIT或Apache-2.0；Rust传递依赖按Cargo.lock及各包许可证处理。
- React、TypeScript、Vite及插件：MIT；npm传递依赖按package-lock.json及各包LICENSE处理。
- xUnit v3 4.0.1：Apache-2.0，仅用于隔离数据测试；版本固定在测试项目 packages.lock.json，不作为产品运行依赖。
- Lucide React 1.52.0：ISC，部分继承Feather图标为MIT；完整许可复制自已安装包，保存在licenses/Lucide.txt，发行时一并分发。
- Tokio 1.53.1：MIT；SHA-2 0.10.9、UUID 1.26.0及Tauri单实例插件2.4.4按Cargo包许可分发。Vitest 5.0.3为MIT，仅开发测试依赖。

用户提供的界面截图仅为施工参考，不自动成为可发行素材。本节点不复制第三方截图到应用发行资源。

- react-activity-calendar 3.2.1：MIT；Apache ECharts 6.1.0及zrender：Apache-2.0/BSD包内声明随安装分发。Tauri dialog 2.7.1：MIT或Apache-2.0，兼容锁定Tauri2.11.5。
- .NET与Windows App SDK采用自包含完整发布目录；WebView2 Evergreen和VC++运行库采用微软签名离线安装器，按微软各自许可提供安装。
- Inno Setup6.7.3仅作为本项目个人非商业构建工具；不把编译器安装目录放入产品。完整依赖清单随Pulse安装目录分发。

## 直接参照与改编来源

- [metasidd/Orb](https://github.com/metasidd/Orb)，Copyright (c) 2024 Siddhant Mehta，MIT。星球视觉、配色和动画参数组织以此为参照，当前 Windows 渲染由 Win2D 实现。保留 [上游许可](licenses/upstream/Orb-LICENSE.txt)。
- [AOSP Launcher3，android-11.0.0_r2](https://android.googlesource.com/platform/packages/apps/Launcher3/+/android-11.0.0_r2/)，Apache-2.0。卡片拖动参考 Workspace 的排序、创建分组、加入分组意图处理；网页卡片的几何与呈现由本项目实现。保留 [原始通知](licenses/upstream/Launcher3-NOTICE.txt)及 [Apache-2.0 正文](licenses/upstream/Apache-2.0.txt)。
- James Clear《原子习惯》为思想参考；论文和作者文章见[README 相关研究](README.md#why)。文献引用不授予或替代代码、图片的许可。

原作者及第三方的许可不因本项目采用 GPL-3.0-only 而被替换，原创代码范围见 [COPYRIGHT.md](COPYRIGHT.md)。
