# 当前接口与历史资料纠偏

核对日期：2026-10-23；依据本地代码，不代表目标服务器部署状态。
以下源文件路径相对于项目根目录。实际使用应读所需服务实现，不把本参考当作永久不变的接口合同。

## Excel导入

来源：backend/Controllers/ImportController.cs、backend/Services/ImportService.cs。

| 请求 | 用途 |
|---|---|
| GET /api/Import/template?entityType={type} | 下载xlsx母版，响应是文件而非统一JSON |
| POST /api/Import/upload?entityType={type} | multipart/form-data，文件字段file，实际写入 |

当前六类及列顺序：

| type | 列顺序 |
|---|---|
| user | 账号、手机号、姓名、角色、初始密码、部门编码（逗号分隔）、人员类型 |
| defect | 名称 |
| operation | 编号、名称、报工部门编码（逗号分隔）、不良品项名称（逗号分隔） |
| routing | 编号、名称、工序编码（按顺序逗号分隔） |
| product | 编号、名称、单位、工艺路线编号、供应商、单价 |
| priceRule | 产品编号、工序编号、部门名称、人员姓名、计价方式、单价、不良扣款、生效时间、失效时间、优先级 |

具体允许空值、枚举、日期及校验以目标母版和当前服务实现为准。角色1管理员/2生产/3班组长；普通工价方式1计件/2计时/3固定，不能把“元/打”误填成方式。

读取的是第一张工作表，第1行作为表头跳过，第2行起按固定列位置取值，不按中文列名自动匹配；母版示例行必须删掉或替换。空行跳过，错误使用Excel实际行号。不要把未经转换的多工作表客户文件直接上传。

正常JSON返回结构为code/msg/data，结果data含successCount、failCount、errors；部分成功仍可能code=0。技术异常可能中断请求，不能断言此前成功行已回滚。用户导入有更新已有账号及部门关系的行为，不是纯新增；同文件重复账号与库中已有账号的处理不同，执行前读ImportUserAsync并展示部门差异。

## 加工选项计价（方案A）

来源：backend/Controllers/OperationPricingController.cs、backend/Services/OperationPricingService.cs、docs/已做指令/280-加工选项正式计价方案A.md、docs/开发手册和方案/33-接口对接说明.md。

| 请求 | 行为 |
|---|---|
| GET /api/operation-pricing/{operationId} | 读取完整ProcessingConfig或null |
| PUT /api/operation-pricing/{operationId} | 保存完整配置，携带读取的version；需要管理员及计件工资授权 |
| GET /api/operation-pricing/product/{productId} | 查询产品路线内已配置工序 |
| POST /api/operation-pricing/{operationId}/preview?productId={productId} | 计算预览，不写业务记录；仍需用户允许的远程访问范围 |

配置含version、groups、bases。groups含id/name/options，options含id/name/extra。bases含id/name/productId/price/deductPrice/priceUnit/reportUnitId/unitsPerPriceUnit/allowed/included。allowed为组ID到允许选项ID数组的映射；included为已含选项ID数组。每组单选，各组可叠加。金额非负且最多4位小数；缺价与显式0不同。unitsPerPriceUnit为一个计价单位对应的报工单位数量，必须与产品报工单位相容。

预览body为baseId及values（组ID到选项ID）；返回含quoteToken。配置写入本身不是“试算”。新建/修改工单锁价需要服务端预览及指纹，不能伪造客户端金额；资料导入不默认创建工单。保存配置后影响新定价，不追改已锁定任务及历史报工。

docs/280明确记录未部署、未做现场SQL Server及HTTP端到端验收。因此未知服务器不能直接批量调用PUT，更不能把404当成空配置。空配置应为成功响应中的null；HTML、401、403、404和技术错误分别处理。

## 旧资料哪些能复用

| 资料 | 可复用 | 不应照搬 |
|---|---|---|
| docs/AI模拟数据/00-新账套模拟数据复用宝典.md | API写入思路、依赖顺序、动态查ID、中文编码经验 | 建账、清旧、报工工资流程、默认密码及历史业务规则 |
| docs/已做指令/223-演示数据-新服务器纯API灌装说明（fid=2太阳神厨具）.md | 远程API与错误排查经验 | fid=2及所有样板ID、自动造数据、跳过TLS校验作为默认行为 |
| docs/已做指令/224-演示数据灌装方法选择总表（本机221vs新服务器223）.md | 区分本机SQL与远程API | 将客户正式资料导入变成模拟数据灌装 |
| docs/已做指令/57-优化指令-批量导入前端补齐.md | 母版与行级错误概念 | “只支持五类”的旧范围；当前另有priceRule |
| tools/build_cj001*.py等历史脚本 | 阅读请求组织方式 | 直接运行，脚本可能包含创建、删除、报工及工资确认 |

路由以控制器Route/Http特性为准，不以“全部复数”或“全部单数”的文字约定猜路径。不要沿用旧文档“已确认工资永不可撤回”等结论；本技能不处理工资，涉及时另查当前PRD、Salary控制器及服务。
