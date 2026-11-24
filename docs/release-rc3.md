# GregShift 1.0-RC3 发布与升级

`1.0-RC3` 是 Windows x64 候选测试版，产品版本为 `1.0.0-rc.3`，不是正式版。EML 的界面名称现为 **GregShift**；启动入口仍是 `EML.exe`，原有实例、设置与账号路径保持兼容。本包尚未进行代码签名。

旧 RC1、RC1-Logo1、RC2 标签、下载附件及原校验值保持不变。RC3 有独立源码提交和校验值，不应混用其他版本的 EXE、DLL 或校验文件。

## 这次主要变化

### GTNH：从旧实例开始升级

在“GTNH 专区 → 版本升级”选择已有游玩数据的旧客户端实例，点击“升级 GTNH · 选择新版”，在独立弹窗里选择更高版本与 Java 变体。确认后留在升级页下载、导入并补全一个**独立的新实例**，无需提前在本机安装新版，也不会覆盖旧实例。已有本地新版时仍可直接选择。

新版先试运行并正常退出，再选择要复制的数据、核对预览与冲突，确认后迁移。可选范围包括世界、NEI、JourneyMap、矿脉与节点记录、服务器列表及部分个人设置。尚未导入旧实例时可使用“导入旧实例（含本地数据）”；重新下载空白安装包不能取回游玩记录。

自己额外添加的模组也可选择迁移，**默认不选，需单独确认兼容风险**。页面区分有额外安装记录与来源待确认的候选，不将旧版独有文件自动视为玩家新增。只复制所选旧文件，不自动升级、不补前置；新版已存在的重复模组保留新版。检查重复不等于兼容性验证。

只迁移语言偏好，不复制旧完整汉化、整包配置和配方脚本。文件复制成功后仍需实际进入游戏验证。跨大版本升级应遵守 GTNH 更新说明，必要时逐版本进入存档。保留旧实例和外部备份；本功能**不包含服务端升级或 SFTP**。

详见[版本升级与玩家数据迁移](https://github.com/Ph1lK4/GregShift/blob/v1.0.0-rc.3/docs/gtnh-player-data-migration.md)。

### 七项 GTNH 工具

左侧“GTNH 专区”下方的“GTNH 工具”无需绑定实例：

| 工具 | 使用方式与边界 |
| --- | --- |
| 工艺流程图 | 本机交互画布、物品图标、动态图集及悬停配方卡片；有效本地缓存可离线阅读，未创建条目保留名称 |
| 在线任务书 | 内嵌网站，查任务与资料版本，不读取或同步游戏进度 |
| 产线规划器 | 在线 GTNH Planner，中文文字辅助、顶部中文搜索及受控方案 JSON 导入/导出 |
| 矿物处理正则 | 原生离线计算，可保存方案；需核对数据适用版本与游戏配方 |
| 神秘时代研究助手 | 原生离线要素连接链搜索，不求解整张研究笔记 |
| GTNH 种子地图 | 内嵌网站，手动输入种子，不自动读取世界或上传文件 |
| 在线魔导手册 | 内嵌研究目录与交互研究图，聚焦内容而非整页网站导航 |

流程图只解析原页面提供的声明式图表与配方数据，不执行数据中的远端代码，也不从连线虚构配方。直接读取遇到 403 时，通过内嵌浏览器正常加载原站并尝试回读；需要人工验证时保留提示，不绕过站点限制。原站结构、网络及资料版本会影响结果，请以实际游戏为准。

Planner 的中文搜索请使用启动器顶部搜索框，下方网页搜索框仍以英文为主。它不是完整离线部署；重要方案应主动导出 JSON。**`cache/gtnh-tools/planner-webview2/` 含用户方案，不是可随意删除的普通下载缓存。** 清理、重装或换电脑前请逐个导出重要方案。

第三方网页、图表、图片与词典仍适用各自许可，不因启动器开源而改用 GPL。详见[工具说明](gtnh-tools.md)、[原生工具与来源](gtnh-native-tools.md)及 [Planner 来源与许可](gtnh-planner-notices.md)。

### 日常界面与使用

支持暗色、亮色与跟随系统，自定义配色和静态壁纸；动画尊重系统动画及高对比度设置。在线模组与整合包采用独立详情页，减少目录拥挤。账号与启动身份支持头像、多正版账号，以及按实例记忆上次模式和账号。正版认证失败不会静默改为离线模式。

实例右键菜单集中详情、文件检查、补全、日志与备份；日常启动不重新计算整包哈希。初始化扫描只更新页面，不弹成功窗口。功能范围与限制见 [README](https://github.com/Ph1lK4/GregShift/blob/v1.0.0-rc.3/README.md)，不表示所有整合包和电脑均已验收。

## 下载包与运行要求

包名为 `EML-1.0-RC3-win-x64.zip`：

```text
EML-1.0-RC3-win-x64/
├── EML.exe                 # 启动入口，名称保留兼容
├── app/                    # 程序、.NET 运行库、语言资源和 WebView2 加载器
├── docs/                   # 源码定位、许可证、隐私、安全与使用说明
├── START-HERE.txt
├── UPGRADE.txt
└── release-manifest.json   # 版本、构建来源与逐文件校验
```

这是 Windows x64 自包含 .NET 发布包，不是单文件 EXE。不需要 Visual Studio、.NET SDK 或另装 .NET；请完整解压并保留 `app/`，不要只复制 `EML.exe`。Java、Minecraft 与整合包不随包附带。将文件夹放在当前用户有写入权限的位置，不要求管理员权限。

网页工具另需 **Microsoft Edge WebView2 Evergreen Runtime**，它不是随包 .NET，也不是包内的 WebView2 加载器。缺少时工具页会提示，不自动运行安装程序；原生工具、有效本地图表缓存与一般实例操作仍可使用。请从 Microsoft 官方 WebView2 页面获取运行时，地址也列在包内 `START-HERE.txt` 中。

发布包不应包含维护者实例、私人日志、下载设置、凭据或浏览器用户配置。公开的微软应用 Client ID 不是玩家凭据。CurseForge 授权仍采用现有程序集内嵌方案，不能将其视为不可提取的秘密；更名与整理目录不改变这一风险。

## 升级启动器：保留数据，合并程序

本节是从旧 EML/GregShift 更新到 RC3，不是升级 GTNH 整合包。建议保留原安装路径；没有自动更新或事务式自动回滚。

1. 正常退出 Minecraft，结束下载、导入、补全和迁移，再退出所有 EML/GregShift 副本。外部启动器启动的游戏也应关闭。
2. 将**整个旧目录**复制到外部备份位置，包含隐藏目录，并确认可读取。备份不要放在即将覆盖的目录中；Planner 重要方案还应逐个导出 JSON。
3. 将 RC3 ZIP 完整解压到新的临时目录，核对同版本 ZIP 的 SHA-256。
4. 将新版包中的程序文件和文件夹复制到旧根目录，合并目录，只覆盖发布包内同名程序文件；保留下面列出的全部玩家数据。
5. 从根目录 `EML.exe` 启动，确认显示 `GregShift 1.0-RC3`，更新旧快捷方式，避免仍启动旧 `EML.App.exe`。
6. 检查实例、账号与模式、Java 路径、内存、JVM 参数、下载及外观设置。第一次游戏验证使用存档副本。

| 数据位置 | 保留要求 |
| --- | --- |
| `instances/` | 实例、世界、地图、截图与游戏设置，完整保留 |
| 实例内 `.eml-runtime/` | 来源、用户变更、启动设置、模组回收区、迁移事务与备份，不当普通缓存删除 |
| `settings/` | 启动器设置，保留 |
| `runtimes/` | 已下载 Java，保留；同路径有助于沿用已存 Java 路径 |
| `cache/` | 下载原包、断点与恢复所需缓存，保留 |
| `cache/gtnh-tools/planner-webview2/` | 含用户方案，先导出重要 JSON，不能随意清理 |
| 根目录 `.eml-runtime/`、`.eml-imports/` | 保留，不在升级中清空 |

不要先删除旧目录，不要“清空后解压”，不要镜像同步或使用 `robocopy /MIR`。不要让两个版本同时操作相同实例，也不要混用新旧 EXE 和 DLL。RC1 升级后可能仍有旧的根目录 DLL、语言文件夹及 `EML.App.exe`；本流程不会自动删除。后续清理应按旧发布清单逐项核对，不能按扩展名批量删除。

**实例 ZIP 不是整个启动器备份**：它不包括实例外共享运行依赖、微软凭据及完整本机迁移事务备份。详见[备份边界](https://github.com/Ph1lK4/GregShift/blob/v1.0.0-rc.3/docs/instance-backups.md)。

### 账号、换位置与回退

微软账号集合仍在当前 Windows 用户的 `%LOCALAPPDATA%\EML\accounts\microsoft.dpapi`，不在发布包或 `app/`。同一 Windows 用户的启动器副本共享该位置；不要分享、搬运凭据或为升级清空它。换电脑或 Windows 用户应重新登录。

换目录不是同路径升级：Java 绝对路径、原整合包位置可能需要重新选择，保留缓存也不意味着所有安装任务自动恢复。新位置核对完成前保留旧目录。

回退前先退出程序并保留错误信息，使用升级前完整备份，避免混用二进制。如果已经继续游玩、创建方案或更改设置，先独立保存新增数据；不要将旧备份直接覆盖到最新世界、方案和设置上。

## 来源、校验与签名

源码仓库为 `https://github.com/Ph1lK4/GregShift`。包内 `docs/SOURCE.txt` 定位对应源码，`release-manifest.json` 记录实际构建来源和逐文件摘要。Release 标签应指向相同源码提交。

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '实际下载的 EML-1.0-RC3-win-x64.zip 路径'
```

将输出与同版本 `.zip.sha256` 对比。摘要可以发现下载损坏或内容变化，不能替代发布者签名或证明第三方内容安全。

**RC3 未进行代码签名。** GitHub 发布、CI 通过和 SHA-256 都不是 Windows 代码签名或安全认证。若 Windows 或安全软件拦截，请核对来源、保留提示并反馈，不建议关闭防护强行运行。见[签名政策](code-signing.md)。

GTNH 国内镜像当前检查下载大小、包格式与路径，没有官方哈希校验；本地摘要不等于官方完整性证明。推荐目录也不代表模组、资源包与目标实例兼容，不自动补齐专区项目的前置依赖。

## 验收范围

测试结果以本次构建实际报告和 Release 说明为准。本说明不预先宣称干净电脑、真实多账号/服务器或真实存档完整升级已经通过；自动化与合成数据测试不能替代这些验收。

| 场景 | 验收重点 |
| --- | --- |
| 干净 Windows x64 | 无 Visual Studio、共享 .NET 或 Java 时普通用户可打开；缺 Java 按确认流程下载，取消不启动 |
| 路径与启动位置 | 中文、空格、快捷方式和不同工作目录下仍读取根目录玩家数据，不进入 `app/` |
| 升级与回退 | 保留实例、设置、Java、来源及迁移记录；已游玩新增进度不被旧备份覆盖 |
| 身份与服务器 | 测试者本人添加真实账号，核对实例记忆、切换、刷新及服务器身份，不静默降级离线 |
| GTNH 完整升级 | 用存档副本走下载、补全、试运行、预览、迁移及再运行，确认源不变、目标与恢复边界 |
| 额外模组 | 默认不选、风险确认、来源待确认与重复跳过可见；不把复制成功当兼容通过 |
| 网页与工具 | 缺 WebView2 提示、原生离线使用、图表缓存、403 正常原站回读及 Planner 连续中文搜索 |
| 方案与故障 | JSON 导入/导出、取消与失败保留已有内容；断网、权限与磁盘错误不报成功 |
| 外观与包隐私 | 深浅主题、输入文字、壁纸和缩放可读；新用户不出现维护者数据 |

不要卸载开发机依赖来模拟干净电脑。测试游戏前关闭其他启动器中的游戏，使用独立副本与外部备份；GregShift 的操作锁不能约束 Prism 或手动运行的进程。自包含包还需持续关注随包 .NET 与依赖补丁。

反馈请提供 GregShift 版本、Windows 版本、包校验值、整合包及 Java 版本、复现步骤和已脱敏错误文字。不要公开密码、令牌、账号文件、服务器私人信息或未经检查的日志。维护者 **K4ssia**；联系与安全漏洞私报：**477619464@qq.com**。

## English summary

GregShift 1.0-RC3 is an unsigned Windows x64 release candidate. The project was previously called EML; the entry point remains `EML.exe` and existing data paths stay compatible. The package includes .NET, but not Java, game files or the WebView2 Evergreen Runtime. Keep the entire `app/` directory.

GTNH upgrades now start from the old client instance. Select a newer version in a dedicated dialog, download and prepare a separate new instance, test it, then choose the player data to copy. Extra mods are optional, unselected by default, and may be incompatible. Existing duplicate mods in the new version are kept. Only the language preference is migrated, not an old translation pack or the pack's full configuration. Server upgrades and SFTP are not included.

Seven GTNH tools cover flowcharts, quests, planning, ore filters, aspect connections, seed maps and research reference. Some use restricted embedded websites; native tools and valid cached flowcharts can work offline. Export important Planner plans as JSON: `cache/gtnh-tools/planner-webview2/` contains user work and must not be treated as disposable cache.

Before updating the launcher, close games and all tasks, back up the entire old folder including hidden data, verify the RC3 ZIP, then merge its program files into the same path without deleting player data. Update shortcuts to the root `EML.exe`. Do not mirror-sync, mix binaries, or overwrite new progress when reverting. Old RC1 and RC2 assets and checksums remain unchanged.

CI and hashes are not code signing or a safety certification. Clean-machine, real-account/server and full real-world upgrade acceptance remain separate testing work. Source and license information are included in `docs/`; GregShift source is licensed under GPL-3.0-or-later, while third-party content retains its own terms.
