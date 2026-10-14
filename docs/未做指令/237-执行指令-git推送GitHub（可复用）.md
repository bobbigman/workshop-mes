# 237-执行指令：git 推送到 GitHub（可复用运行手册）

> 状态：待做
> 性质：内部试用（代码备份）
> 本次交付：提交 + 推送到远程 `master`；**不含**改历史
> 目标：让 Cursor 在 `D:\workspace\pkub\bee` 把已授权改动安全推送到 GitHub，不泄漏 appsettings / 令牌 / 连接串
> 依据/拍板：2026-10-11 已跑通；远程 `https://github.com/bobbigman/workshop-mes.git`；分支 `master`；本机壳 **Windows PowerShell 5.1**（不用 bash、不用 `&&`）

## 取舍（已定）

| 项 | 方案及依据 | 状态 |
|---|---|---|
| 远程/分支 | `github.com/bobbigman/workshop-mes.git` / `master`，不改 remote | 已定 |
| 登录 | 凭据管理器已登过 GitHub，直接 `git push`，不写 Token | 已定 |
| 机密 | 提交前必须从暂存区拿掉 appsettings* / 连接串 / 令牌 / order_list 等 | 已定 |
| 本机壳 | PowerShell 5.1；提交说明用 here-string | 已定 |

## 本次范围与落点

- 交付：一条完整流程【看状态 → 暂存并剔除机密 → 提交 → 推送 → 汇报】
- 已有：`.gitignore` 已挡 `bin/ obj/ node_modules/ *.dll/ wwwroot/assets/ *.zip/ __pycache__/ mcp_query*.py/ docs/服务器信息/appsettings.json/ AI助手APIKey部署配置说明.md/ appsettings.json.bak`
- 已知风险：`tools/verify_hy_mcp_key.py` **已跟踪且硬编码了 MCP Bearer 令牌**（旧账）；推送前勿把该文件新内容带进提交，历史清理需用户另行决定

## 行为与口径（分叉写成显式条件）

- 当状态是 `ahead` 且工作区干净 → 直接 `git push origin master`
- 当有未提交改动 → 先提交安全改动，再推
- 当已经 `ahead` 同时还有未提交改动 → 先提交安全改动，把已有提交和新提交一起推
- 当**没有上游分支** → 停下来告诉用户，不自己猜地址
- 当 `git diff --cached --name-only` 命中 `appsettings|\.env|secret|token|password|\.bak|mcp_query|order_list|_tok|_login` → 对该文件 `git reset HEAD -- -- "文件路径"`，确认暂存区无它再提交
- 当推送失败（认证/网络，如 `Recv failure`、连接重置）→ 贴错误原文，可原样重试一次 `git push origin master`，再失败就停

## 止步线

- 不 `git push --force`、不 `git commit --amend`
- 不把 appsettings、连接串、密码、Token 推进远程
- 不改历史（filter-branch 等）；`tools/verify_hy_mcp_key.py` 的令牌历史清理需用户另行拍板

## 验收（可观察）

- [ ] 推送输出含 `master -> master`，`git status -sb` 不再显示 `ahead`
- [ ] 已给出提交 hash（`git log -1 --oneline`）
- [ ] `git diff --cached --name-only` 复查：appsettings* / 令牌 / 连接串不在提交文件清单
- [ ] 已告知留在本机未推送的文件（至少含 `backend/appsettings*.json`）

## 交付终点

- 交付：提交 hash + 远程已是 `master` + 未推送文件清单；异常时报告错误原文与停手点
- 未包含改历史、清令牌等动作，需用户另行授权
