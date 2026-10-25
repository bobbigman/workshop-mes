-- 仅在核实远端运行进程的有效连接（含环境变量覆盖）后执行。
-- 默认只检查并回滚；记录并安全保存目标行结果后，才可设 @Execute=1。
-- 不包含连接串。禁止改为本机库试删；请使用 sqlcmd -b 或遇错即停止的客户端。
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Execute bit = 0;
DECLARE @ExpectedServer nvarchar(128) = N''; -- 核实后的 @@SERVERNAME
DECLARE @ExpectedDatabase nvarchar(128) = N''; -- 核实后的 DB_NAME()
IF @ExpectedServer = N'' OR @ExpectedDatabase = N''
    THROW 51400, N'414：尚未填写经部署端确认的服务器和数据库，禁止执行', 1;
IF @ExpectedServer <> CONVERT(nvarchar(128), @@SERVERNAME) OR @ExpectedDatabase <> DB_NAME()
    THROW 51400, N'414：当前连接与已核实目标不一致', 1;
IF @@TRANCOUNT <> 0 THROW 51400, N'414：必须在无外层事务的新连接执行', 1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRY
    BEGIN TRAN;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock
        @Resource=N'operation-price-list-factory-4', @LockMode=N'Exclusive',
        @LockOwner=N'Transaction', @LockTimeout=15000;
    IF @LockResult < 0 THROW 51400, N'414：获取价目表事务锁失败', 1;

    DECLARE @Codes table(code nvarchar(64) PRIMARY KEY);
    INSERT @Codes VALUES(N'PL-BB-01'),(N'PL-BD-01'),(N'PL-BZ-HY004'),(N'PL-DC-01'),
        (N'PL-PJ-HY004'),(N'PL-PJZ-01'),(N'PL-YB-01'),(N'PL-ZSB-01');
    SELECT p.* INTO #Targets FROM dbo.base_operation_price_list p WITH(UPDLOCK,HOLDLOCK)
        JOIN @Codes c ON c.code=p.code WHERE p.factory_id=4;
    IF (SELECT COUNT(*) FROM #Targets) <> 8 OR EXISTS(SELECT 1 FROM #Targets WHERE status<>2)
        THROW 51400, N'414：目标不是恰好8张停用表，禁止删除', 1;
    IF EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id=OBJECT_ID(N'dbo.base_operation_price_list') AND is_disabled=0)
        THROW 51400, N'414：价目表存在启用的触发器，须先审核其副作用', 1;

    -- 对全库所有非目标价目表保留完整行，事务内对比；不依赖过期的预期清单。
    SELECT p.* INTO #Preserved FROM dbo.base_operation_price_list p WITH(UPDLOCK,HOLDLOCK)
        WHERE NOT EXISTS(SELECT 1 FROM #Targets t WHERE t.id=p.id);

    -- 全库检查，不能因引用记录厂别异常而漏检；共享锁维持至事务结束。
    SELECT t.code, COUNT_BIG(i.id) AS salary_refs FROM #Targets t
        LEFT JOIN dbo.salary_statement_item i WITH(HOLDLOCK) ON i.price_list_id=t.id GROUP BY t.code;
    IF EXISTS(SELECT 1 FROM dbo.salary_statement_item i WITH(HOLDLOCK) JOIN #Targets t ON i.price_list_id=t.id)
        THROW 51400, N'414：存在工资明细引用，禁止删除', 1;

    CREATE TABLE #Snapshots(source nvarchar(32), payload nvarchar(max));
    INSERT #Snapshots SELECT N'工单任务',processing_price FROM dbo.prod_work_order_operation WITH(HOLDLOCK)
        WHERE processing_price IS NOT NULL;
    INSERT #Snapshots SELECT N'报工',price_snapshot FROM dbo.prod_report WITH(HOLDLOCK)
        WHERE price_snapshot IS NOT NULL;
    IF EXISTS(SELECT 1 FROM #Snapshots WHERE ISJSON(payload)<>1)
        THROW 51400, N'414：存在无效计价快照JSON，无法完整排除引用', 1;

    -- 递归检查所有 priceListId（大小写兼容），覆盖任务与报工嵌套快照。
    ;WITH nodes AS (
        SELECT source, [key], value, type FROM #Snapshots CROSS APPLY OPENJSON(payload)
        UNION ALL
        SELECT n.source,j.[key],j.value,j.type FROM nodes n
            CROSS APPLY OPENJSON(CASE WHEN n.type IN(4,5) THEN n.value ELSE N'{}' END) j
    )
    SELECT n.source,t.id,t.code INTO #JsonRefs FROM nodes n JOIN #Targets t
        ON LOWER(n.[key])=N'pricelistid' AND TRY_CONVERT(bigint,n.value)=t.id
        OPTION(MAXRECURSION 100);
    SELECT source,code,COUNT_BIG(*) AS snapshot_refs FROM #JsonRefs GROUP BY source,code;
    IF EXISTS(SELECT 1 FROM #JsonRefs) THROW 51400, N'414：存在工单或报工快照引用，禁止删除', 1;

    SELECT @@SERVERNAME AS server_name,DB_NAME() AS database_name;
    SELECT * FROM #Targets ORDER BY code; -- 执行前安全保存完整目标行，不提交业务数据到Git。
    IF @Execute=0
    BEGIN
        ROLLBACK;
        SELECT N'仅检查通过，未删除；先保存目标行，再显式设 Execute=1' AS result;
    END
    ELSE
    BEGIN
        DELETE p FROM dbo.base_operation_price_list p JOIN #Targets t ON p.id=t.id
            WHERE p.factory_id=4 AND p.status=2;
        DECLARE @Deleted int=@@ROWCOUNT;
        IF @Deleted<>8 THROW 51400, N'414：删除行数不为8，回滚', 1;
        IF EXISTS(SELECT 1 FROM dbo.base_operation_price_list p JOIN #Targets t ON p.id=t.id)
            THROW 51400, N'414：删除后仍有目标行，回滚', 1;
        IF EXISTS(SELECT * FROM #Preserved EXCEPT SELECT * FROM dbo.base_operation_price_list)
            OR EXISTS(SELECT * FROM dbo.base_operation_price_list EXCEPT SELECT * FROM #Preserved)
            THROW 51400, N'414：非目标价目表发生变化，回滚', 1;
        SELECT id,factory_id,code,name,status FROM dbo.base_operation_price_list WHERE factory_id=4 ORDER BY code;
        COMMIT;
        SELECT @Deleted AS deleted_rows,N'已提交' AS result;
    END;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
