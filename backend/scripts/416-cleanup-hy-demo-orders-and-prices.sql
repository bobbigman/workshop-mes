-- 416：仅限用户已确认的HY演示旧工单及旧价目表；默认排练并回滚。
-- 先备份完整8单/93任务/8价表，填写该JSON原始UTF-16LE字节的SHA256。
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Execute bit=0;
DECLARE @ExpectedTargetSha nvarchar(64)=N'';
IF LEN(@ExpectedTargetSha)<>64 THROW 51600,N'416：缺少已核实的完整备份SHA256',1;
IF CONVERT(nvarchar(128),@@SERVERNAME)<>N'WIN-S8B0B5KANV5' OR DB_NAME()<>N'WorkShopMes'
 THROW 51600,N'416：不是已确认的远程数据库',1;
IF @@TRANCOUNT<>0 THROW 51600,N'416：需要无外层事务的新连接',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
 BEGIN TRAN;
 DECLARE @lock int;
 EXEC @lock=sys.sp_getapplock @Resource=N'operation-price-list-factory-4',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 51600,N'416：获取同厂价目表事务锁失败',1;
 DECLARE @OrderKeys table(id bigint PRIMARY KEY,order_no nvarchar(64));
 INSERT @OrderKeys VALUES(77,N'A0001'),(78,N'A0002'),(79,N'A0003'),(80,N'A0004'),(81,N'A0005'),(82,N'A0006'),(83,N'A0007'),(84,N'A0008');
 DECLARE @PriceKeys table(id bigint PRIMARY KEY,code nvarchar(64));
 INSERT @PriceKeys VALUES(15,N'PL-BB-01'),(20,N'PL-BD-01'),(28,N'PL-BZ-HY004'),(14,N'PL-DC-01'),(27,N'PL-PJ-HY004'),(21,N'PL-PJZ-01'),(16,N'PL-YB-01'),(25,N'PL-ZSB-01');
 SELECT o.* INTO #Orders FROM dbo.prod_work_order o WITH(UPDLOCK,HOLDLOCK) JOIN @OrderKeys k ON k.id=o.id AND k.order_no=o.order_no WHERE o.factory_id=4;
 SELECT t.* INTO #Tasks FROM dbo.prod_work_order_operation t WITH(UPDLOCK,HOLDLOCK) JOIN #Orders o ON o.id=t.work_order_id;
 SELECT p.* INTO #Prices FROM dbo.base_operation_price_list p WITH(UPDLOCK,HOLDLOCK) JOIN @PriceKeys k ON k.id=p.id AND k.code=p.code WHERE p.factory_id=4;
 IF (SELECT COUNT(*) FROM #Orders)<>8 OR EXISTS(SELECT 1 FROM #Orders WHERE status<>0)
  THROW 51600,N'416：目标工单不为8张未开始工单',1;
 IF (SELECT COUNT(*) FROM #Tasks)<>93 THROW 51600,N'416：工单任务不为93条',1;
 IF (SELECT COUNT(*) FROM #Prices)<>8 OR EXISTS(SELECT 1 FROM #Prices WHERE status<>2)
  THROW 51600,N'416：目标价目表不为8张停用表',1;
 IF EXISTS(SELECT 1 FROM sys.triggers WHERE is_disabled=0 AND parent_id IN(OBJECT_ID(N'dbo.prod_work_order'),OBJECT_ID(N'dbo.prod_work_order_operation'),OBJECT_ID(N'dbo.base_operation_price_list')))
  THROW 51600,N'416：目标表存在启用触发器，须先审核副作用',1;

 DECLARE @payload nvarchar(max)=(SELECT
  JSON_QUERY((SELECT * FROM #Orders ORDER BY id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS orders,
  JSON_QUERY((SELECT * FROM #Tasks ORDER BY id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS tasks,
  JSON_QUERY((SELECT * FROM #Prices ORDER BY id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS prices FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
 IF CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),@payload)),2)<>@ExpectedTargetSha
  THROW 51600,N'416：目标与完整备份内容不一致，禁止删除',1;

 -- 查全部现有逻辑工单/任务引用列，不假定只有外键。任务表本身在授权清单内。
 CREATE TABLE #Dependencies(object_name nvarchar(300),record_count bigint);
 DECLARE @check nvarchar(max)=N'';
 SELECT @check=@check+N'INSERT #Dependencies SELECT N'''+REPLACE(s.name+N'.'+t.name+N'.'+c.name,N'''',N'''''')+N''',COUNT_BIG(*) FROM '+QUOTENAME(s.name)+N'.'+QUOTENAME(t.name)+N' WITH(HOLDLOCK) WHERE '+QUOTENAME(c.name)+N' IN(SELECT id FROM '+CASE WHEN LOWER(c.name)=N'work_order_operation_id' THEN N'#Tasks' ELSE N'#Orders' END+N');'
 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
 WHERE LOWER(c.name) IN(N'order_id',N'work_order_id',N'work_order_operation_id')
  AND t.object_id<>OBJECT_ID(N'dbo.prod_work_order_operation');
 EXEC sys.sp_executesql @check;
 IF EXISTS(SELECT 1 FROM #Dependencies WHERE record_count<>0)
  THROW 51600,N'416：存在额外工单或任务依赖，禁止自动扩删',1;
 IF EXISTS(SELECT 1 FROM dbo.sys_custom_field_value v WITH(HOLDLOCK) JOIN dbo.sys_custom_field f WITH(HOLDLOCK) ON f.id=v.field_id
  WHERE (f.target=N'work_order' AND v.target_id IN(SELECT id FROM #Orders)) OR (f.target=N'work_order_task' AND v.target_id IN(SELECT id FROM #Tasks)))
  THROW 51600,N'416：存在工单或任务自定义字段值，停止',1;
 IF EXISTS(SELECT 1 FROM dbo.salary_statement_item i WITH(HOLDLOCK) JOIN #Prices p ON p.id=i.price_list_id)
  THROW 51600,N'416：存在工资来源引用，停止',1;

 -- 在Serializable锁范围内保留完整非目标行；操作后不允许旁改其他对象。
 SELECT * INTO #KeepOrders FROM dbo.prod_work_order WITH(UPDLOCK,HOLDLOCK) WHERE id NOT IN(SELECT id FROM #Orders);
 SELECT * INTO #KeepTasks FROM dbo.prod_work_order_operation WITH(UPDLOCK,HOLDLOCK) WHERE id NOT IN(SELECT id FROM #Tasks);
 SELECT * INTO #KeepPrices FROM dbo.base_operation_price_list WITH(UPDLOCK,HOLDLOCK) WHERE id NOT IN(SELECT id FROM #Prices);
 SELECT * INTO #Reports FROM dbo.prod_report WITH(HOLDLOCK);
 SELECT * INTO #SalaryItems FROM dbo.salary_statement_item WITH(HOLDLOCK);

 DELETE t FROM dbo.prod_work_order_operation t JOIN #Tasks x ON x.id=t.id;
 DECLARE @DeletedTasks int=@@ROWCOUNT;
 IF @DeletedTasks<>93 THROW 51600,N'416：任务删除行数不为93，回滚',1;
 DELETE o FROM dbo.prod_work_order o JOIN #Orders x ON x.id=o.id WHERE o.factory_id=4 AND o.status=0;
 DECLARE @DeletedOrders int=@@ROWCOUNT;
 IF @DeletedOrders<>8 THROW 51600,N'416：工单删除行数不为8，回滚',1;

 -- 删除价目表前重新扫描全部剩余工单与报工的自包含JSON依据。
 CREATE TABLE #Snapshots(payload nvarchar(max));
 INSERT #Snapshots SELECT processing_price FROM dbo.prod_work_order_operation WITH(HOLDLOCK) WHERE processing_price IS NOT NULL;
 INSERT #Snapshots SELECT price_snapshot FROM dbo.prod_report WITH(HOLDLOCK) WHERE price_snapshot IS NOT NULL;
 IF EXISTS(SELECT 1 FROM #Snapshots WHERE ISJSON(payload)<>1) THROW 51600,N'416：存在无效快照JSON，回滚',1;
 ;WITH nodes AS(
  SELECT [key],value,type FROM #Snapshots CROSS APPLY OPENJSON(payload)
  UNION ALL SELECT j.[key],j.value,j.type FROM nodes n CROSS APPLY OPENJSON(CASE WHEN n.type IN(4,5) THEN n.value ELSE N'{}' END) j
 )
 SELECT p.id INTO #RemainingRefs FROM nodes n JOIN #Prices p ON LOWER(n.[key])=N'pricelistid' AND TRY_CONVERT(bigint,n.value)=p.id OPTION(MAXRECURSION 100);
 IF EXISTS(SELECT 1 FROM #RemainingRefs) THROW 51600,N'416：仍有快照引用旧价目表，全部回滚',1;
 DELETE p FROM dbo.base_operation_price_list p JOIN #Prices x ON x.id=p.id WHERE p.factory_id=4 AND p.status=2;
 DECLARE @DeletedPrices int=@@ROWCOUNT;
 IF @DeletedPrices<>8 THROW 51600,N'416：价目表删除行数不为8，回滚',1;

 IF EXISTS(SELECT * FROM #KeepOrders EXCEPT SELECT * FROM dbo.prod_work_order) OR EXISTS(SELECT * FROM dbo.prod_work_order EXCEPT SELECT * FROM #KeepOrders)
  THROW 51600,N'416：非目标工单发生变化，回滚',1;
 IF EXISTS(SELECT * FROM #KeepTasks EXCEPT SELECT * FROM dbo.prod_work_order_operation) OR EXISTS(SELECT * FROM dbo.prod_work_order_operation EXCEPT SELECT * FROM #KeepTasks)
  THROW 51600,N'416：非目标任务发生变化，回滚',1;
 IF EXISTS(SELECT * FROM #KeepPrices EXCEPT SELECT * FROM dbo.base_operation_price_list) OR EXISTS(SELECT * FROM dbo.base_operation_price_list EXCEPT SELECT * FROM #KeepPrices)
  THROW 51600,N'416：非目标价目表发生变化，回滚',1;
 IF EXISTS(SELECT * FROM #Reports EXCEPT SELECT * FROM dbo.prod_report) OR EXISTS(SELECT * FROM dbo.prod_report EXCEPT SELECT * FROM #Reports)
  THROW 51600,N'416：报工记录发生变化，回滚',1;
 IF EXISTS(SELECT * FROM #SalaryItems EXCEPT SELECT * FROM dbo.salary_statement_item) OR EXISTS(SELECT * FROM dbo.salary_statement_item EXCEPT SELECT * FROM #SalaryItems)
  THROW 51600,N'416：工资明细发生变化，回滚',1;
 IF @Execute=1 COMMIT; ELSE ROLLBACK;
 SELECT @Execute AS committed,@DeletedTasks AS deleted_tasks,@DeletedOrders AS deleted_orders,@DeletedPrices AS deleted_prices,
  CONVERT(nvarchar(128),@@SERVERNAME) AS server_name,DB_NAME() AS database_name;
 SELECT factory_id,COUNT_BIG(*) AS price_list_count FROM dbo.base_operation_price_list GROUP BY factory_id ORDER BY factory_id;
 SELECT id,code,status FROM dbo.base_operation_price_list WHERE factory_id=4 ORDER BY code;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK;
 THROW;
END CATCH;
