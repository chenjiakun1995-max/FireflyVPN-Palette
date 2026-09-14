# FireflyVPN-Palette 收藏版

开发分支：`codex/favorites`。收藏逻辑位于共享的 ServiceLib，WPF 与 Avalonia 节点列表共用它。

## 使用

- 点击节点前的星标收藏或取消收藏；点击上方“★ 收藏”筛选。
- 右键“编辑收藏备注”，保存或取消。清空备注恢复原名。
- 备注在前，订阅原名以较小、较淡的文字紧跟在同一行；超长内容截断，悬停可查看完整文字。
- 收藏独立于订阅节点 ID。仅改名、重排或重新生成 ID，不会丢失收藏。
- 端口、认证、传输或 TLS 等连接配置不同，需要“查看并关联新配置”。候选并不代表已确认是同一台服务器。
- 节点消失后保留收藏，标记未找到。确认关联只更新收藏，双击节点才连接。
- 收藏配置不保证固定出口 IP；出口变化记录独立于收藏身份。

## 保存与匹配

`guiConfigs/palette-favorites.db` 保存来源、备注、版本化配置指纹和加密快照。
Windows 使用现有的当前用户 DPAPI 保护；不会把凭据写进收藏名称、变化提示或日志。
连接中的收藏另存已批准配置，防止订阅刷新或编辑收藏时切换当前连接。
订阅导入失败会恢复上一份节点记录和自定义配置文件。

`PaletteBuild=true` 的独立构建显示 FireflyVPN-Palette 名称；启用 LocalAppData 模式时使用
`%LOCALAPPDATA%/FireflyVPN-Palette`，便携模式使用程序所在目录，不迁移原版数据。

## 验证

需要 .NET 10 SDK：

```powershell
dotnet test Firefly/ServiceLib.Tests/ServiceLib.Tests.csproj
dotnet build Firefly/Firefly/Firefly.csproj
dotnet run --project Firefly/FavoriteUi.Smoke/FavoriteUi.Smoke.csproj
```

最后一项使用 Avalonia Headless 和合成节点，在测试程序自己的输出目录创建数据库。
它不实例化正式的 App、MainWindow 或 CoreManager，不启动代理核心，也不设置系统代理。
覆盖星标、键盘操作、备注编辑、筛选、单行布局、配置更新确认和失败订阅导入。
预览图输出到 `artifacts/favorites/`。

## 独立打包

设置已获授权的后台地址到 `FIREFLY_CLIENT_API_URL` 环境变量，准备经过校验的 Windows 核心目录，然后运行：

```powershell
./scripts/build-palette.ps1 -CoreDirectory 'path/to/bin'
```

脚本只生成新的便携目录和 ZIP，不启动或安装程序。首次使用时，在准备切换 VPN 后自行运行新版本。
后台地址、个人节点、设备凭据和运行数据均不应提交到公开仓库。

合并上游时，重点复查订阅导入、默认节点选择和自动恢复的调用点，再执行上述验证。
