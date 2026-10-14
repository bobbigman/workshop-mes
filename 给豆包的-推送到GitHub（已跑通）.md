# 给豆包：把车间小工单推送到 GitHub

这份是 **2026-10-11 已经推成功** 的做法。成功结果是远程出现 `master -> master`。按下面顺序做，不要换写法。

仓库目录：`D:\workspace\pkub\bee`  
远程：`https://github.com/bobbigman/workshop-mes.git`  
分支：`master`  
本机壳：Windows PowerShell 5.1（不要用 bash，不要用 `&&`）  
登录：本机已经用 Windows 凭据管理器登过 GitHub。直接 `git push`，不要改 remote，不要在地址里写 Token，不要改 `git config`。

## 1. 先看现状

在仓库根目录执行：

```powershell
cd D:\workspace\pkub\bee
git status -sb
git log -5 --oneline
git rev-parse --abbrev-ref '@{u}'
```

`@{u}` 必须加单引号。不加引号，PowerShell 会把命令跑坏。

怎么往下走：

| 看到什么 | 做什么 |
|---|---|
| 状态是 `ahead`，工作区干净 | 跳到第 5 步，直接推 |
| 没有可推的提交，但有未提交改动 | 从第 2 步做：先提交，再推 |
| 已经 `ahead`，同时还有未提交改动 | 先提交安全改动，再把已有提交和新提交一起推 |
| 没有上游分支 | 停下来告诉用户，不要自己猜地址 |

## 2. 暂存，再把密钥拿出去

`.gitignore` 已经挡住了 `bin/`、`obj/`、`node_modules/`、`*.dll`、`backend/wwwroot/assets/`、`backend/_*.json`、`.tmp/`。  
下面这些文件 **有的已经被 git 跟踪**，`git add -A` 仍会把它们的新内容放进提交。必须在提交前拿出来。

```powershell
git add -A
git reset HEAD -- backend/appsettings.json backend/appsettings.WorkshopMesB.json backend/appsettings.json.bak
git reset HEAD -- -- order_list.txt
git rm --cached -f -- "mcp_query.py" "mcp_query - 副本.py"
```

`git rm --cached` 只是从本次提交里拿掉，不删本机文件。某个文件本来就不在暂存区里、命令报路径不存在，跳过该文件，继续往下做。

提交前再查一遍暂存区：

```powershell
git diff --cached --name-only | Select-String -Pattern 'appsettings|\.env|secret|token|password|\.bak|mcp_query|order_list|_tok|_login'
```

有任何一行命中：对那个文件执行 `git reset HEAD -- -- "文件路径"`，确认暂存区里没有它，再提交。

## 3. 提交说明

说明写为什么，一两句。用 PowerShell 的 here-string。结束的 `"@` 必须单独占一行，顶格写。

```powershell
git commit -m @"
Backup accumulated MES work without secrets.

<这里写一句这次为什么要备份>
"@
```

不要用 bash 的 `$(cat <<'EOF' ... EOF)`，PowerShell 5.1 不认，提交会失败。

## 4. 推送

```powershell
git push origin master
git status -sb
```

成功时，输出里有 `master -> master`，随后 `git status -sb` 不再显示 `ahead`。

把这三样告诉用户：

- 提交短 hash（`git log -1 --oneline`）
- 远程已是 `master`
- 仍留在本机、没有推进去的文件（至少包括 `backend/appsettings*.json`）

## 5. 推失败时

认证失败或网络中断（例如 `Recv failure`、空回复、连接被重置）：把错误原文贴给用户，请用户处理后再说「继续」。可以再试一次同样的 `git push origin master`。第二次还失败就停。

停手清单：

- 不要 `git push --force`
- 不要 `git commit --amend`
- 不要改 remote 地址
- 不要改 `git config`
- 不要把 `appsettings`、连接串、密码、Token 推进远程
