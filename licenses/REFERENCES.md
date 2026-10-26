# 第三方材料来源

本目录保留上游许可证、版权通知及其来源映射。原文属于各自权利人；habitOrbit 的 GPL-3.0-only 声明不替代这些条款。

- `package-sources.json` 按包名与锁定版本列出补充文件及来源。Rust 优先使用包中 `.cargo_vcs_info.json` 的提交；Microsoft.Data.Sqlite 使用 NuGet 声明的 dotnet/dotnet 提交；SQLitePCLRaw 使用 v2.1.12。缺少已确认版本引用的来源保留实际获取的上游快照，不冒充特定历史版本。
- `upstream/` 内带 Git blob 缩写的文件来自同一上游 blob，多个包可复用同一份正文。许可证说明、标准正文和锁定包原始作者声明分别保留，标准 MIT 文本中的占位符未被替上游自行填写。
- `upstream/WindowsSDK-BuildTools-LICENSE.rtf` 取自包内声明的 [微软许可入口](https://aka.ms/WinSDKLicenseURL)，同名 TXT 由 Windows RichTextBox 的 RTF 读取器提取，以便阅读；RTF 是原始文本来源。
- `upstream/Win2D-LICENSE.txt` 是 [微软 Win2D 官方源码许可](https://github.com/microsoft/Win2D/blob/winappsdk/main/LICENSE.txt)的 MIT 文本。微软官方 [README](https://github.com/microsoft/Win2D/blob/winappsdk/main/README.md) 明确链接 MIT，官方 [CHANGELOG](https://github.com/microsoft/Win2D/blob/winappsdk/main/CHANGELOG.md) 记录 Win2D-WinUI 1.4.0 及从 2015 年起采用 MIT。NuGet 1.4.0 自带旧 licenseUrl 已重定向，分发时保留原始声明与官方 MIT 正文。
- `upstream/Orb-LICENSE.txt` 取自 [metasidd/Orb](https://github.com/metasidd/Orb/blob/main/LICENSE)，保留 Siddhant Mehta 的署名。
- `upstream/Launcher3-NOTICE.txt` 原样解码自 [AOSP android-11.0.0_r2 的 NOTICE](https://android.googlesource.com/platform/packages/apps/Launcher3/+/android-11.0.0_r2/NOTICE?format=TEXT)，Apache-2.0 正文取自 [Apache 官方](https://www.apache.org/licenses/LICENSE-2.0.txt)。
- `upstream/MPL-2.0.txt` 取自 [Mozilla 官方](https://www.mozilla.org/media/MPL/2.0/index.txt)，用于包内声明为 MPL-2.0 的 selectors；对应源码许可头单独保留。

实际分发清单由 `scripts/collect-licenses.ps1` 生成，包含包内原文、补充原文与必要的原始包声明。清单保留构建和其他平台依赖，不能以清单条目数推定运行程序包含哪些组件。
