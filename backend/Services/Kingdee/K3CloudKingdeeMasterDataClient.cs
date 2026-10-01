using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ahu.MicrosoftMes.Common;

namespace ahu.MicrosoftMes.Services.Kingdee;

/// <summary>
/// 金蝶云星空 WebAPI 只读主数据（docs/72）。
/// 登录 + ExecuteBillQuery；FormId 可配置。演示服预先配通，上门不连客户账。
/// </summary>
public class K3CloudKingdeeMasterDataClient : IKingdeeMasterDataClient
{
    public const string HttpClientName = "kingdee";
    public string ModeName => "K3Cloud";

    private readonly HttpClient _http;
    private readonly KingdeeOptions _opt;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private bool _loggedIn;

    public K3CloudKingdeeMasterDataClient(IHttpClientFactory httpFactory, IOptions<KingdeeOptions> opt)
    {
        _http = httpFactory.CreateClient(HttpClientName);
        _opt = opt.Value;
    }

    public async Task<KingdeeMaterialDto?> GetMaterialAsync(string code, CancellationToken ct = default)
    {
        code = (code ?? "").Trim();
        if (code.Length == 0) return null;
        await EnsureLoginAsync(ct);

        // FNumber, FName, FRefCost（参考成本；现场无此字段可改查询列）
        var rows = await ExecuteBillQueryAsync(
            _opt.MaterialFormId,
            "FNumber,FName,FRefCost",
            $"FNumber='{EscapeFilter(code)}'",
            ct);

        if (rows.Count == 0) return null;
        var row = rows[0];
        return new KingdeeMaterialDto
        {
            Code = Cell(row, 0),
            Name = Cell(row, 1),
            CostPrice = ParseDec(Cell(row, 2))
        };
    }

    public async Task<KingdeeBomCostDto?> GetBomMaterialCostAsync(string code, CancellationToken ct = default)
    {
        code = (code ?? "").Trim();
        if (code.Length == 0) return null;
        await EnsureLoginAsync(ct);

        // 常见 BOM 单据字段；现场字段名差异时改配置/查询串
        var rows = await ExecuteBillQueryAsync(
            _opt.BomFormId,
            "FMATERIALID.FNumber,FTreeEntity_FMATERIALIDCHILD.FNumber,FTreeEntity_FMATERIALIDCHILD.FName,FTreeEntity_FNUMERATOR,FTreeEntity_FMATERIALIDCHILD.FRefCost",
            $"FMATERIALID.FNumber='{EscapeFilter(code)}'",
            ct);

        if (rows.Count == 0) return null;

        var dto = new KingdeeBomCostDto { ParentCode = code };
        foreach (var row in rows)
        {
            var line = new KingdeeBomLineDto
            {
                MaterialCode = Cell(row, 1),
                MaterialName = Cell(row, 2),
                Qty = ParseDec(Cell(row, 3)),
                UnitCost = ParseDec(Cell(row, 4))
            };
            if (string.IsNullOrWhiteSpace(line.MaterialCode) && line.Qty == 0 && line.UnitCost == 0)
                continue;
            dto.Lines.Add(line);
        }
        dto.TotalCost = dto.Lines.Sum(x => x.LineCost);
        return dto.Lines.Count == 0 ? null : dto;
    }

    public async Task<List<KingdeeRoutingStepDto>> GetRoutingAsync(string code, CancellationToken ct = default)
    {
        code = (code ?? "").Trim();
        if (code.Length == 0) return new List<KingdeeRoutingStepDto>();
        await EnsureLoginAsync(ct);

        var rows = await ExecuteBillQueryAsync(
            _opt.RoutingFormId,
            "FMaterialId.FNumber,FEntity_FSeq,FEntity_FProcessId.FName",
            $"FMaterialId.FNumber='{EscapeFilter(code)}'",
            ct);

        var list = new List<KingdeeRoutingStepDto>();
        foreach (var row in rows)
        {
            var name = Cell(row, 2);
            if (string.IsNullOrWhiteSpace(name)) continue;
            list.Add(new KingdeeRoutingStepDto
            {
                Seq = (int)ParseDec(Cell(row, 1)),
                OperationName = name
            });
        }
        return list.OrderBy(x => x.Seq).ToList();
    }

    private async Task EnsureLoginAsync(CancellationToken ct)
    {
        if (_loggedIn) return;
        await _loginLock.WaitAsync(ct);
        try
        {
            if (_loggedIn) return;
            if (string.IsNullOrWhiteSpace(_opt.BaseUrl) || string.IsNullOrWhiteSpace(_opt.AcctId))
                throw ThrowHelper.BizUser("金蝶未配置 BaseUrl/AcctId，请检查演示服 appsettings 的 Kingdee 节");

            var url = Combine(_opt.BaseUrl, "Kingdee.BOS.WebApi.ServicesStub.AuthService.ValidateUser.common.kdsvc");
            var bodyObj = new
            {
                acctID = _opt.AcctId,
                username = _opt.UserName,
                password = _opt.Password,
                lcid = _opt.Lcid
            };
            var json = JsonSerializer.Serialize(bodyObj);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            HttpResponseMessage resp;
            string respText;
            try
            {
                resp = await _http.SendAsync(req, ct);
                respText = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                throw ThrowHelper.Api(url, RedactLogin(json), ex.Message, ex);
            }

            if (!resp.IsSuccessStatusCode)
                throw ThrowHelper.Api(url, RedactLogin(json), $"HTTP {(int)resp.StatusCode} {respText}");

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(respText) ? "{}" : respText);
            var root = doc.RootElement;
            var loginOk = root.TryGetProperty("LoginResultType", out var lrt) && lrt.GetInt32() == 1
                          || root.TryGetProperty("IsSuccessByAPI", out var ok) && ok.ValueKind == JsonValueKind.True;
            // 部分版本：LoginResultType=1 成功；也有放在 Result.ResponseStatus
            if (!loginOk)
            {
                if (root.TryGetProperty("LoginResultType", out var t) && t.ValueKind == JsonValueKind.Number && t.GetInt32() == 1)
                    loginOk = true;
            }
            if (!loginOk && root.TryGetProperty("Message", out var msgEl))
                throw ThrowHelper.Api(url, RedactLogin(json), respText);

            // ValidateUser 成功时常返回 LoginResultType=1
            if (root.TryGetProperty("LoginResultType", out var lr) && lr.ValueKind == JsonValueKind.Number)
            {
                if (lr.GetInt32() != 1)
                    throw ThrowHelper.Api(url, RedactLogin(json), respText);
            }

            _loggedIn = true;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task<List<JsonElement>> ExecuteBillQueryAsync(string formId, string fieldKeys, string filter, CancellationToken ct)
    {
        var url = Combine(_opt.BaseUrl, "Kingdee.BOS.WebApi.ServicesStub.DynamicFormService.ExecuteBillQuery.common.kdsvc");
        var payload = new
        {
            data = new
            {
                FormId = formId,
                FieldKeys = fieldKeys,
                FilterString = filter,
                TopRowCount = 200
            }
        };
        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage resp;
        string respText;
        try
        {
            resp = await _http.SendAsync(req, ct);
            respText = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            throw ThrowHelper.Api(url, json, ex.Message, ex);
        }

        if (!resp.IsSuccessStatusCode)
            throw ThrowHelper.Api(url, json, $"HTTP {(int)resp.StatusCode} {respText}");

        if (string.IsNullOrWhiteSpace(respText) || respText == "[]")
            return new List<JsonElement>();

        using var doc = JsonDocument.Parse(respText);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw ThrowHelper.Api(url, json, respText);

        // 失败时常返回 [{"Result":{"ResponseStatus":{"IsSuccess":false,...}}}]
        if (doc.RootElement.GetArrayLength() > 0)
        {
            var first = doc.RootElement[0];
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("Result", out _))
                throw ThrowHelper.Api(url, json, respText);
        }

        var list = new List<JsonElement>();
        foreach (var row in doc.RootElement.EnumerateArray())
            list.Add(row.Clone());
        return list;
    }

    private static string Cell(JsonElement row, int index)
    {
        if (row.ValueKind != JsonValueKind.Array || index >= row.GetArrayLength()) return "";
        var el = row[index];
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.Null => "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString()
        };
    }

    private static decimal ParseDec(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0m;
        return decimal.TryParse(s, out var v) ? v : 0m;
    }

    private static string EscapeFilter(string s) => s.Replace("'", "''");

    private static string Combine(string baseUrl, string path)
    {
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static string RedactLogin(string json)
    {
        // 简单脱敏 password 字段
        return System.Text.RegularExpressions.Regex.Replace(
            json,
            "\"password\"\\s*:\\s*\"[^\"]*\"",
            "\"password\":\"***\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
