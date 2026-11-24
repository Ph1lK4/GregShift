# 隐私说明 / Privacy notice

更新日期：2026-11-22。适用于当前 EML 源码；已发布版本的行为以对应版本说明为准。

EML 是面向 Minecraft、尤其是 GTNH 玩家的 Windows 启动器。公开维护者为 **K4ssia**，联系及安全漏洞私报邮箱为 **[477619464@qq.com](mailto:477619464@qq.com)**。

目前 EML 没有维护者自建的数据接收服务，也没有启用捐赠或独立反馈平台。这不等于启动器不联网：正版登录、在线搜索、图标、文件补全和下载会访问相应的第三方服务。

## 本机保存和处理的数据

便携发布版通常将实例与设置保存在程序目录下；源码调试版或用户另外选择的文件可能位于其他位置。删除前请核对实际路径。

| 数据 | 用途与位置 |
| --- | --- |
| 实例与玩家数据 | `instances/` 内的世界、地图、服务器列表、截图、模组、资源包、游戏设置及日志，用于游戏和实例管理 |
| 启动与文件状态 | 实例内 `.eml-runtime/` 保存启动模式、公开玩家名和 UUID、上次游戏进程启动时间、内存/JVM 参数、Java 路径、文件来源和完整性记录、用户修改及迁移记录；其中可能有备份，不是普通缓存，不保存登录令牌 |
| 启动器与下载数据 | `settings/`、`cache/`、根目录 `.eml-runtime/` 和 `.eml-imports/` 中的设置、图标缓存、下载缓存、断点文件、原包及工作数据；不是所有任务列表状态都会持久化 |
| Java 环境 | 检测本机 Java 的路径、版本与架构，供启动时选择；EML 下载的 Java 位于 `runtimes/java/` |
| 微软登录记录 | `%LOCALAPPDATA%\EML\accounts\microsoft.dpapi`，整体加密保存多个 Minecraft 玩家档案、各自的应用 Client ID 与刷新令牌，以及默认账号选择 |
| GTNH 流程图数据 | `cache/gtnh-tools/flowcharts/` 保存按需读取并校验的图表、配方 JSON、来源地址、原站修订号（如有）、读取时间与许可标记，以及原站物品图片缓存；不保存原网页、脚本或玩家进度 |
| GTNH 工具网页数据 | `cache/gtnh-tools/webview2/` 保存内嵌阅读器的 Cookie、网页缓存及网站本地存储；与微软账号凭据和系统浏览器配置分开 |
| GTNH 原生工具设置 | `settings/gtnh-tools/` 保存用户主动保存的矿物处理方案和研究助手偏好；本地计算使用内置数据，不读取存档或研究进度，导入/导出仅操作用户选择的 JSON 文件 |
| GTNH 产线规划器数据 | `cache/gtnh-tools/planner-webview2/` 独立保存规划器的本机方案、网站设置、Cookie 和缓存；包含用户内容，清理或卸载前先逐个导出重要方案 JSON；导入/导出只读取或写入用户选择的方案文件 |

Java 检测和实例检查会读取本机相关文件或环境信息，完整性检查也会计算文件摘要。平台文件识别功能可能向平台查询摘要；这与上传整个本机文件不同。

EML 的账号存储使用当前 Windows 用户范围的 DPAPI 加密，不将微软密码保存到该文件。当前持久化账号模型不保存短期 Minecraft access token。DPAPI 不是对同一 Windows 用户下所有程序的隔离保证；不要分享凭证文件，也不要运行来源不明的模组或程序。

从其他启动器复制实例时，会读取所选实例的名称、目录、版本及游戏文件；对支持的 Modrinth App 数据库只查询实例与组件记录，不读取账号表。原启动器的账号凭证、启动命令和应用数据库不作为游戏内容导入。游戏或模组自建配置可能仍包含私人信息，只导入你信任的实例。

## 正版登录

微软登录在微软授权页面完成，EML 不要求在自己的界面输入微软密码。登录及会话刷新会连接 Microsoft、Xbox Live/XSTS 和 Minecraft 服务，交换该流程必需的授权码或令牌，并获取玩家档案及游戏访问相关信息。

启动器打开时读取本机账号记录，不据此主动刷新会话；用户进行正版登录或正版启动时会按流程联网。正版启动还需要把玩家身份及游戏会话信息传给本机 Minecraft 进程。因此本机进程、游戏或模组对这些信息的访问不能仅靠凭证文件加密来阻止。

离线模式使用用户填写的玩家名，不等于获得正版授权，也不能保证进入要求正版认证的服务器。

账号页与正版启动身份选择显示时，EML 会用公开的 Minecraft UUID 向 `sessionserver.mojang.com` 查询皮肤地址，再从 `textures.minecraft.net` 获取皮肤并在本机生成头像。这些头像请求不发送登录令牌、密码或离线玩家名，也不访问第三方头像网站；不会为获取头像刷新登录会话。皮肤缓存位于启动器根目录 `.eml-runtime/account-cache/avatars/`，通常六小时内复用；获取失败可使用已有有效缓存，否则显示内置 Steve。离线模式使用内置 Steve，不为头像联网。移除登录记录不会自动删除这些公开皮肤缓存；关闭启动器后可删除此头像缓存目录。

## 搜索、下载及第三方服务

下表概括当前主要联网用途，不是穷尽的域名白名单。实际文件、图标和运行库可能由接口或整合包清单指定的 CDN、项目发布页及其他主机提供。

| 服务 | 用途及相关请求信息 |
| --- | --- |
| Microsoft、Xbox、Minecraft | 账号授权、会话刷新、玩家档案及游戏访问相关请求 |
| Modrinth、CurseForge | 搜索整合包/模组、版本与依赖查询、文件识别和下载；涉及搜索词、筛选条件、项目/版本/文件 ID，以及部分用于匹配的文件摘要 |
| Minecraft/Mojang、Prism 元数据、Fabric、Quilt 及清单中的下载源 | 获取 Minecraft 版本、加载器、运行库、资源和完整性元数据，下载所选实例的运行依赖 |
| Adoptium / Eclipse Temurin 及其文件下载源 | 查询匹配的 Java 版本、操作系统和架构，下载 Java |
| GitHub、GTNH 国内镜像及项目下载源 | GTNH 整合包、可添加模组、资源包等的发布信息查询和下载；当前镜像下载使用 `downloads.gaytnh.com` |
| 接口提供的图片地址 | 显示项目图标等内容，并在本机缓存 |
| GTNH 中文维基及其图片服务 | 首次打开未缓存的已创建流程图或主动检查更新时请求对应公开页面，只解析声明式图表/配方数据；图片按需从 `huiji-public.huijistatic.com`、`huiji-thumb.huijistatic.com` 的 GTNH 图片目录获取；数据请求不使用浏览器 Cookie 或微软登录凭据 |
| GTNH 中文维基、GTNH 在线任务书及其网页引用的内容服务 | 主动更新目录、查看原站或打开在线任务书时通过内嵌浏览器联网；网站可接收访问路径、IP 地址、浏览器请求头及其自身 Cookie 等常规网页信息 |
| GTNH Planner（`gtnhplanner.com`）及其引用的配方、图片和社区服务 | 用户打开产线规划器后通过独立内嵌浏览器联网；网站及其服务接收常规浏览器请求、站内搜索及用户在网页中主动进行的操作，适用其自身政策；不附带启动器微软令牌或实例数据 |
| GTNH 种子地图（`chunkbasenh.beyomoes.com`）及其网页内容服务 | 用户打开种子地图后内嵌浏览器联网；网站可处理用户主动输入的种子及常规浏览器请求。不自动读取存档或上传本地文件 |

这些服务在收到请求时可获得 IP 地址、请求路径和请求头等网络信息。第三方如何处理和保留请求信息，取决于其自身政策，不由 EML 保证。请在使用相关服务前查阅其官方网站的隐私说明。

部分功能会在浏览器打开 Wiki、GitHub、GitLab、Discord、OptiFine 或其他项目页面。浏览器已有的账号、Cookie、扩展和授权适用浏览器及网站自己的设置；关闭 EML 不会清除它们。EML 启动的 Minecraft、模组及服务器也可能自行联网，其行为不属于这份启动器说明所能完整覆盖的范围。

GTNH 工具位于独立导航页，不需要选择实例。流程图默认由本机 WPF 画布绘制，只解析原站公开页面中的图表、物品图标和配方资料，不运行原站 Lua、JavaScript 或模板源码。有效图表和图片缓存优先使用；重新打开已缓存图表不主动请求原站，检查更新需要用户操作。图表与图片缓存和原站来源、修订及许可记录一起保存在上述独立目录，不包含账号信息或游戏数据；清除此目录会失去相应离线副本，下次需联网重新读取。

直接请求被原站拒绝时，启动器会正常加载对应原站页面，成功后自动读取固定元素中的图表、配方和图片地址，校验后返回本机画布。若原站要求验证，请自行完成后点击“读取为本地图表”。不会额外获取账号或实例数据。原站浏览过程仍会使用阅读器自己的 Cookie、脚本和网站存储；这不是无 Cookie 的直接数据请求，也不绕过原站访问限制。

只有主动更新目录、显式查看原站或打开在线任务书、在线魔导手册和种子地图时，工具才使用 WebView2 显示 `gtnh.huijiwiki.com`、`gtnhquestsbook.top` 和 `chunkbasenh.beyomoes.com`。阅读器不复用系统浏览器或微软登录配置，不向网页提供读取实例、存档、账号令牌或执行本机命令的接口，不同步游戏任务进度。网页可以按浏览器正常行为加载自己的脚本、图片及内容服务，第三方站点的处理规则仍适用；顶层站外跳转、文件下载及网站权限请求默认被阻止。关闭启动器不会自动清空这个独立网页配置，清理方法见卸载说明。WebView2 运行时由 Microsoft 提供并适用其政策；启动器不自动安装它，缺少它不影响本地工具。

“产线规划器”另用一个 WebView2 实例，仅在用户选择该工具后打开 `https://gtnhplanner.com/`，不复用上述阅读器、系统浏览器或微软登录配置。网站正常执行自己的程序并访问其内容服务；方案、搜索词、注释及其他用户在网页中输入的内容可由原站脚本处理。GregShift 的中文适配脚本只修改已识别文字及操作公开搜索和方案文件入口，不读取启动器登录令牌、账号目录、游戏存档或实例文件。中英文名称词典随程序提供，不向翻译服务发送文本；这不等于原站网页本身不会联网。

规划器方案由原站保存在独立本机网站存储中。用户主动点击“导入方案”时，启动器读取所选 JSON，检查大小及结构后交给该网页的公开导入入口；它会进入第三方网页运行环境，不应包含密码或令牌。主动点击“导出方案”时，启动器从原站公开导出流程取得 JSON，校验后由用户选择保存路径；普通网页下载仍全部阻止，不向网站开放任意本机文件服务。当前不保证社区登录或云同步，也不把本机存储等同于绝不外传的保密环境。关闭启动器不会删除本机方案；卸载或清理 `planner-webview2/` 前请先导出重要方案，详见[卸载说明](docs/uninstall.md)。

## 使用统计、日志与主动反馈

本轮对当前源码及项目引用的检查未发现主动使用统计或自动上传崩溃报告的实现；这不是完整的网络流量审计，也不代表第三方登录、搜索、下载或游戏组件不会传输信息。需要正式发布时的验证仍应通过实际联网验收完成。

错误日志可能包含本机用户名、路径、服务器地址、游戏聊天或第三方模组输出。EML 不会自动把这些日志、存档或截图发送至维护者邮箱。请在主动联系时只发送排查必需的信息，并先去除令牌、凭证、私人路径及其他敏感内容。

用户主动发送到上述邮箱的内容会由邮件服务处理，并供维护者沟通和排查使用。如需讨论邮件或附件的删除，请通过同一邮箱联系。这里不承诺未经确认的固定保留期限，也不承诺能够删除第三方服务独立持有的记录。

## 用户选择与删除

- 可以不登录微软账号；离线模式的使用范围受游戏及服务器要求限制。
- 可以不使用在线搜索、镜像或 Java 下载；但缺少依赖的实例仍需要通过适当方式补齐文件。
- 本地设置、实例、缓存和备份通常保留在磁盘上，关闭程序不会自动清除。删除前应保留所需存档和来源记录；部分缓存原包可用于恢复包内文件。
- 在“微软账号”页面选择账号并确认“移除所选账号”，只删除该账号的本机登录记录，保留其他账号。实例记住的公开玩家名、UUID 和启动历史不会因此删除，也不会自动改成另一个账号。
- 同一 Windows 用户下的 EML 副本共用上述凭证文件。完整清除需逐个移除账号，或关闭全部 EML 后按卸载说明删除整份凭证文件；后者会清除该 Windows 用户所有 EML 副本共用的账号记录。移除不会撤销浏览器登录、微软侧的应用授权，或保证已启动的游戏会话立即失效。
- 需要完整移除程序及凭证时，参见[卸载与保留玩家数据](docs/uninstall.md)。不要把删除程序和删除存档视为同一选择。

隐私问题可联系 **K4ssia / 477619464@qq.com**；疑似漏洞请按[安全报告说明](SECURITY.md)私密报告。本文说明当前实现，不是合规认证或绝对安全承诺。数据处理方式改变时应同步更新本文。

## English summary

EML is maintained by **K4ssia**. Contact **477619464@qq.com** for privacy questions or private security reports. There is currently no maintainer-operated data collection server, donation service or separate feedback platform.

Instances, worlds, settings, file provenance, migration backups, caches and downloaded Java runtimes are stored locally. Per-instance launch settings remember the public player name, UUID, mode and last game-process start time, without login tokens. Multiple Microsoft profiles, their refresh tokens and the default account choice are stored together at `%LOCALAPPDATA%\EML\accounts\microsoft.dpapi`, protected with Windows current-user DPAPI. EML does not save your Microsoft password in that file. Encryption does not isolate account data from every program running as the same Windows user.

Folder imports read selected game files and supported launcher instance metadata, not launcher account records. Launcher credentials, application databases and launch commands are excluded from the copied game content. Private information stored by individual mods may still be present; only import instances you trust.

Sign-in uses Microsoft, Xbox and Minecraft services. Search and download features contact Modrinth, CurseForge, Minecraft runtime and loader services, Adoptium, GitHub, GTNH mirrors and project-specified hosts. Requests can include search filters, project/file IDs and file hashes used for matching. Recipients also receive ordinary network request information. Their retention practices, browser sessions and network activity of Minecraft/mods are outside EML's control.

Visible online-account avatars use public Minecraft UUIDs to request skin metadata from `sessionserver.mojang.com` and skins from `textures.minecraft.net`, without login tokens or third-party avatar services. Skin files are cached locally under `.eml-runtime/account-cache/avatars/` and normally reused for six hours; stale valid skins may be used if refresh fails. Offline mode uses a built-in Steve face without avatar requests. Removing an account does not automatically remove its public skin cache; the avatar-cache directory may be deleted after closing EML.

GTNH flowcharts use a native WPF canvas. Opening an uncached chart or explicitly checking for updates requests its public wiki page without browser cookies or Microsoft credentials and reads declarative chart and recipe data. It does not execute remote Lua, JavaScript or template source. Item icons are fetched on demand from the GTNH upload paths at `huiji-public.huijistatic.com` and `huiji-thumb.huijistatic.com`, subject to URL, size and image limits. Valid cached charts and images are preferred without a network request. The separate `cache/gtnh-tools/flowcharts/` directory keeps chart, recipe and image data, source, revision when available, retrieval time and license metadata, not game progress or account data. Deleting it removes offline copies.

If direct retrieval returns HTTP 403, the launcher automatically opens the corresponding original page in its embedded browser and reads the chart after normal loading, then returns to the native canvas. Existing cache remains available. If the site requires human verification, complete it yourself before choosing “Read as native chart”; access controls are not bypassed. Only declarative chart, icon and recipe data and revision metadata from the confirmed page are read and validated before caching. The original page still uses the embedded browser's cookies, scripts and site storage, unlike the cookie-free direct request.

Online catalog updates, original pages, the quest book and Thaumonomicon use WebView2 for `gtnh.huijiwiki.com` and `gtnhquestsbook.top`; the seed map uses `chunkbasenh.beyomoes.com`. These pages and their referenced services receive ordinary browser requests and can process information entered into them, including seeds. Cookies, web caches and site storage use the separate `cache/gtnh-tools/webview2/` profile, not the system browser or Microsoft-login profile. No host bridge grants access to game files, login tokens or local commands; game progress is not synchronized. Off-site top-level navigation, downloads and permission requests are blocked by default. The native ore-regex and research tools calculate from bundled data without network requests or reading game files; explicitly saved preferences go in `settings/gtnh-tools/`. Close the launcher before deleting tool data. WebView2 is separate from .NET and is not installed automatically; native tools do not depend on it.

The production planner opens `https://gtnhplanner.com/` in a separate WebView2 instance only when selected. Its recipe, image and community services receive normal browser requests, and site scripts can process searches, plans, annotations and other content entered in that page. The dedicated `cache/gtnh-tools/planner-webview2/` profile keeps locally saved plans, site settings, cookies and caches, separate from the reader, system browser and Microsoft-login profiles. It does not receive launcher account tokens or instance files. Bundled Chinese dictionaries adapt recognized labels and public search/file controls without sending text to a translation service; the website itself still uses the network.

Planner JSON import and export require a launcher-button action and a user-selected file path. Imported JSON is bounded and checked before being passed to the site's public import control, so it becomes available to third-party page scripts; do not include credentials or private secrets. Export obtains JSON through the site's public export action, validates it and saves it only at the chosen path. Direct browser downloads remain blocked, and no arbitrary local-file bridge is exposed. Community login and cloud synchronization are not guaranteed. Local site storage is not a promise that site content can never be transmitted. **Export important plans before clearing the planner profile or uninstalling**; closing the launcher does not delete them. See the [uninstallation guide](docs/uninstall.md).

The current source review found no active usage-analytics or automatic crash-upload implementation; this is not a complete traffic audit. Feedback is sent only when you choose to contact the maintainer. Redact logs and never send credentials or full private instances. Contact the same mailbox to discuss removal of information you have emailed.

Removing a selected account removes only that account's local login record. Other accounts and the public identity remembered by instances remain. All EML copies under the same Windows user share the credential file; deleting the whole file after closing EML clears all their saved accounts. Neither action revokes browser sessions or Microsoft-side consent. Back up player data before removing the portable directory. See the [uninstallation guide](docs/uninstall.md) and [security policy](SECURITY.md).
