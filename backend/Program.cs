using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using ahu.MicrosoftMes.Common;
using ahu.MicrosoftMes.Data;
using ahu.MicrosoftMes.Mcp;
using ahu.MicrosoftMes.Services;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

// 引导日志：配置完成前故障写 stderr
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // 黑窗口（start.bat）要能看中文启动提示
    Console.OutputEncoding = Encoding.UTF8;

    var builder = WebApplication.CreateBuilder(args);

    var instanceSection = builder.Configuration.GetSection(InstanceOptions.SectionName);
    var instanceOpt = instanceSection.Get<InstanceOptions>() ?? new InstanceOptions();
    builder.Services.Configure<InstanceOptions>(instanceSection);
    builder.Services.AddSingleton(new InstanceRuntime());

    var jwtSection = builder.Configuration.GetSection("Jwt");
    var jwtOpt = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
    builder.Services.Configure<JwtOptions>(jwtSection);

    var aiSection = builder.Configuration.GetSection(AiOptions.SectionName);
    builder.Services.Configure<AiOptions>(aiSection);

    var errorLogSection = builder.Configuration.GetSection(ErrorLoggingOptions.SectionName);
    var errorLogOpt = errorLogSection.Get<ErrorLoggingOptions>() ?? new ErrorLoggingOptions();
    builder.Services.Configure<ErrorLoggingOptions>(errorLogSection);
    builder.Services.Configure<SupportOptions>(
        builder.Configuration.GetSection(SupportOptions.SectionName));
    errorLogOpt.Validate();
    var logDir = ErrorLoggingBootstrap.PrepareDirectory(errorLogOpt, builder.Environment.ContentRootPath, instanceOpt.Code);
    var logPath = ErrorLoggingBootstrap.BuildFilePathTemplate(logDir);

    builder.Services.Configure<DeploymentOptions>(
        builder.Configuration.GetSection(DeploymentOptions.SectionName));
    var trialCleanupSection = builder.Configuration.GetSection(TrialCleanupOptions.SectionName);
    builder.Services.Configure<TrialCleanupOptions>(trialCleanupSection);
    (trialCleanupSection.Get<TrialCleanupOptions>() ?? new TrialCleanupOptions()).Validate();

    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        var opt = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ErrorLoggingOptions>>().Value;
        var inst = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<InstanceOptions>>().Value;
        cfg.MinimumLevel.Is(LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Instance", inst.Code)
            .Enrich.WithProperty("Application", "WorkshopMes")
            .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
            .WriteTo.File(
                new CompactJsonFormatter(),
                logPath,
                restrictedToMinimumLevel: LogEventLevel.Warning,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: opt.FileSizeLimitBytes,
                retainedFileCountLimit: null, // 保留策略由 ErrorLogCleanupService 按 UTC 日桶处理
                shared: true,
                encoding: Encoding.UTF8);
    });

    InstanceConfigValidator.Validate(instanceOpt, jwtOpt);

    builder.Services.AddSingleton<SqlCommandErrorInterceptor>();
    builder.Services.AddSingleton<OutboundHttpErrorLogger>();
    builder.Services.AddTransient<OutboundHttpLoggingHandler>();
    builder.Services.AddHostedService<ErrorLogCleanupService>();
    builder.Services.AddHostedService<TrialAccountCleanupService>();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    if (instanceOpt.EnableSwagger)
    {
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "微聚 API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "先调 POST /api/Auth/login（请求体 { account, password }，可选 factoryCode）拿到 token"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });
    }

    builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
    {
        opt.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
        opt.AddInterceptors(sp.GetRequiredService<SqlCommandErrorInterceptor>());
    });

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOpt.Issuer,
                ValidAudience = jwtOpt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOpt.Secret))
            };
        });
    builder.Services.AddAuthorization();

    var licenseCloudSection = builder.Configuration.GetSection(LicenseCloudOptions.SectionName);
    builder.Services.Configure<LicenseCloudOptions>(licenseCloudSection);

    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<ILicenseCloudService, LicenseCloudService>();
    builder.Services.AddScoped<ILicenseTierService, LicenseTierService>();
    builder.Services.AddHttpClient(LicenseCloudService.HttpClientName, (sp, c) =>
        {
            var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LicenseCloudOptions>>().Value;
            c.Timeout = TimeSpan.FromSeconds(Math.Max(2, opt.TimeoutSeconds));
        })
        .AddHttpMessageHandler<OutboundHttpLoggingHandler>();
    builder.Services.AddScoped<IFactoryService, FactoryService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IDepartmentService, DepartmentService>();
    builder.Services.AddScoped<IUnitService, UnitService>();
    builder.Services.AddScoped<IDefectItemService, DefectItemService>();
    builder.Services.AddScoped<IOperationService, OperationService>();
    builder.Services.AddScoped<IRoutingService, RoutingService>();
    builder.Services.AddScoped<IProductService, ProductService>();
    builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
    builder.Services.AddScoped<IReportService, ReportService>();
    builder.Services.AddScoped<IReviewService, ReviewService>();
    builder.Services.AddScoped<IAssignService, AssignService>();
    builder.Services.AddScoped<IPriceRuleService, PriceRuleService>();
    builder.Services.AddScoped<ISalaryService, SalaryService>();
    builder.Services.AddScoped<IReportStatService, ReportStatService>();
    builder.Services.AddScoped<IExecutionMonitorService, ExecutionMonitorService>();
    builder.Services.AddScoped<IAbnormalService, AbnormalService>();
    builder.Services.AddScoped<IAiReportEvidenceService, AiReportEvidenceService>();
    builder.Services.AddScoped<ICustomFieldService, CustomFieldService>();
    builder.Services.AddScoped<IImportService, ImportService>();
    builder.Services.AddScoped<IPrintSettingService, PrintSettingService>();
    builder.Services.AddScoped<IWechatAlertService, WechatAlertService>();
    builder.Services.AddScoped<WechatMessageSender>();
    builder.Services.AddScoped<WechatEventService>();
    builder.Services.AddScoped<IWorkerViewService, WorkerViewService>();
    builder.Services.AddScoped<IKingdeeAuthService, FakeKingdeeAuthService>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IMcpRequestContext, McpRequestContext>();
    builder.Services.AddScoped<IMcpKeyService, McpKeyService>();
    builder.Services.AddScoped<IMcpWxBindService, McpWxBindService>();
    builder.Services.AddScoped<IMcpAuthService, McpAuthService>();
    builder.Services.AddScoped<IScheduleScoreService, ScheduleScoreService>();
    builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
    builder.Services.AddScoped<IKnowledgeFileService, KnowledgeFileService>();
    builder.Services.Configure<ahu.MicrosoftMes.Services.Kingdee.KingdeeOptions>(
        builder.Configuration.GetSection(ahu.MicrosoftMes.Services.Kingdee.KingdeeOptions.SectionName));
    var kingdeeMode = builder.Configuration["Kingdee:Mode"] ?? "Mock";
    if (string.Equals(kingdeeMode, "K3Cloud", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddScoped<ahu.MicrosoftMes.Services.Kingdee.IKingdeeMasterDataClient, ahu.MicrosoftMes.Services.Kingdee.K3CloudKingdeeMasterDataClient>();
    else
        builder.Services.AddScoped<ahu.MicrosoftMes.Services.Kingdee.IKingdeeMasterDataClient, ahu.MicrosoftMes.Services.Kingdee.MockKingdeeMasterDataClient>();
    builder.Services.AddScoped<IQuoteSuggestService, QuoteSuggestService>();
    builder.Services.AddHttpClient(ahu.MicrosoftMes.Services.Kingdee.K3CloudKingdeeMasterDataClient.HttpClientName, (sp, c) =>
        {
            var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ahu.MicrosoftMes.Services.Kingdee.KingdeeOptions>>().Value;
            c.Timeout = TimeSpan.FromSeconds(Math.Max(10, opt.TimeoutSeconds));
            if (!string.IsNullOrWhiteSpace(opt.BaseUrl))
                c.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer()
        })
        .AddHttpMessageHandler<OutboundHttpLoggingHandler>();
    builder.Services.AddSingleton<KnowledgeFileParser>();
    builder.Services.AddSingleton<AiSessionStore>();
    builder.Services.AddSingleton<SupportDocumentService>();
    builder.Services.AddScoped<AiToolDispatcher>();
    builder.Services.AddScoped<IDeepSeekClient, DeepSeekClient>();
    builder.Services.AddScoped<IAiAssistantService, AiAssistantService>();
    builder.Services.AddScoped<IDiagSqlService, DiagSqlService>();

    builder.Services.AddHttpClient(DeepSeekClient.HttpClientName, (sp, c) =>
        {
            var ai = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
            c.Timeout = TimeSpan.FromSeconds(Math.Max(10, ai.RequestTimeoutSeconds));
        })
        .AddHttpMessageHandler<OutboundHttpLoggingHandler>();

    if (instanceOpt.EnableMcp)
    {
        builder.Services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithToolsFromAssembly();
    }

    // 群Webhook由发送器记录已知key脱敏后的上下文，避免通用传输日志先记录原始错误。
    builder.Services.AddHttpClient(WechatMessageSender.GroupHttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(40);
        })
        .RemoveAllLoggers();

    builder.Services.AddHttpClient("wecom", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
        })
        .AddHttpMessageHandler<OutboundHttpLoggingHandler>();

    builder.Services.AddCors(opt =>
    {
        opt.AddDefaultPolicy(p => p
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(TraceIdMiddleware.HeaderName));
    });

    var app = builder.Build();

    app.UseMiddleware<TraceIdMiddleware>();
    app.UseMiddleware<ExceptionMiddleware>();
    // API 响应禁止缓存，包含鉴权失败和未命中接口时的 SPA 兜底 HTML（docs/111）。
    // 放在鉴权/静态文件之前，OnStarting 确保最终响应带 no-store。
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
        }
        await next();
    });
    app.UseCors();
    app.UseMiddleware<McpGateMiddleware>();
    if (instanceOpt.EnableMcp)
        app.UseMiddleware<McpTokenMiddleware>();
    if (instanceOpt.EnableSwagger)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "微聚 API v1"));
    }
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<InstanceFactoryGuardMiddleware>();
    app.UseMiddleware<RoleGuardMiddleware>();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapControllers();
    if (instanceOpt.EnableMcp)
        app.MapMcp("/mcp");
    app.MapFallbackToFile("index.html");

    using (var scope = app.Services.CreateScope())
    {
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var runtime = sp.GetRequiredService<InstanceRuntime>();

        try
        {
            Console.WriteLine("正在连接数据库并初始化…");
            db.Database.EnsureCreated();
            await DbCompat.EnsureAsync(db);

            if (instanceOpt.BootstrapOnStartup)
                await InstanceBootstrap.EnsureAsync(db, instanceOpt, logger);

            if (instanceOpt.SeedDemoData)
                await SeedData.EnsureAsync(db);
            else if (!db.Factories.Any(f => f.FactoryCode == instanceOpt.FactoryCode))
                logger.LogWarning(
                    "工厂 {FactoryCode} 不存在且未开启 SeedDemoData。请设 Instance:BootstrapOnStartup=true 完成首次初始化后改回 false。",
                    instanceOpt.FactoryCode);

            var factory = db.Factories.FirstOrDefault(f => f.FactoryCode == instanceOpt.FactoryCode);
            if (factory != null)
                runtime.FactoryId = factory.Id;
            else
                logger.LogWarning("未能解析本实例工厂 Id，已登录请求的工厂校验将跳过直至初始化完成");
        }
        catch (Exception ex)
        {
            Console.WriteLine("数据库初始化失败，详见下方错误与 logs 目录。");
            Log.Fatal(ex, "启动数据库初始化失败 Directory={LogDir}", logDir);
            throw;
        }
    }

    var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:8080";
    Console.WriteLine($"已启动 · 实例 {instanceOpt.Code} · 监听 {urls}");
    Console.WriteLine($"本机打开 http://localhost:8080/  · 错误日志 {logDir}");
    Console.WriteLine("本窗口不要关；关掉即停程序。");
    Log.Warning("WorkshopMes 已启动 Instance={Instance} ErrorLogDir={LogDir} Urls={Urls}", instanceOpt.Code, logDir, urls);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "应用程序终止");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
