# GTNH Planner 来源与许可 / Sources and notices

更新日期：2026-11-22。适用于 GregShift 当前源码中的产线规划器接入，不代表已发布 RC2 已包含此功能。

## 在线规划器

- 服务：[GTNH Planner](https://gtnhplanner.com/)。
- 上游源码：[jackwrichards/gtnh-factory-flow](https://github.com/jackwrichards/gtnh-factory-flow)。
- 本次适配参考版本：v4.7.0，提交 `e493e36a64d5a226e6110fab8a765ae08c3bf7c5`。这是接入时核对的源码版本，不是对在线站点版本的锁定；网站可能独立更新。
- 上游程序声明使用 MIT 许可证。GregShift 通过内嵌浏览器访问在线服务，没有将上游完整应用、配方数据库或图标素材随发布包复制分发。数据与素材不能仅因程序采用 MIT 就推定全部适用同一许可，仍应核对各自来源和授权。

GTNH Planner 与其社区服务由第三方维护，并非 GregShift 或 GTNH 官方提供的保证。网页内容及请求适用对应站点规则。当前接入不保证社区登录、云同步、离线可用或与任意实例版本的配方兼容。

## 中文词典

- 来源：[NanShang126/gtnh-planner-zh](https://github.com/NanShang126/gtnh-planner-zh)。
- 提取参考提交：`70f68d550189b42af4fd7b0592d9340aa5f5633b`。
- 从该项目提取名称及界面词典，不执行或附带其用户脚本。名称词典含 32,120 项，另有 71 个原始界面词条；GregShift 的文字适配代码补充部分当前界面文案。
- 随源码保留原始 MIT 许可证：`src/EML.App/Resources/GtnhPlanner/LICENSE.txt`。Windows 发布包在 `docs/LICENSE-GTNH-PLANNER-ZH.txt` 中附带相同内容。
- `names.json` SHA-256：`4154a580ef875ce751c29ab53990f1b65e43285207d2a1e65b87aad0a6a88186`。
- `ui.json` SHA-256：`34e9cf565c16e15db931092de2850e0c5e48c42d545f10cf376dae2b8217105f`。

词典不是所有 GTNH 版本的完整翻译，可能有缺项、同名或过时译名。保留英文切换和候选原名，不能用译名替代游戏物品 ID 或配方兼容性判断。中文显示不翻译用户方案名和注释。

## English summary

GregShift embeds the online service at `gtnhplanner.com`; it does not bundle the full GTNH Planner application, recipe database or icon collection. The integration was checked against `jackwrichards/gtnh-factory-flow` v4.7.0, commit `e493e36a64d5a226e6110fab8a765ae08c3bf7c5`. The application declares an MIT license; this does not assign the same license to all recipe or image sources. The live website can change independently.

Bundled name and UI dictionaries were extracted from `NanShang126/gtnh-planner-zh`, commit `70f68d550189b42af4fd7b0592d9340aa5f5633b`, without executing or bundling its user script. Its original MIT notice is retained in `src/EML.App/Resources/GtnhPlanner/LICENSE.txt` and distributed as `docs/LICENSE-GTNH-PLANNER-ZH.txt`. The dictionary contains 32,120 names and 71 original UI terms; GregShift adds some current UI wording in its own adapter. Translations may be incomplete or outdated. Upstream content and bundled dictionaries retain their own licenses, separate from GregShift's GPL-3.0-or-later source license.
