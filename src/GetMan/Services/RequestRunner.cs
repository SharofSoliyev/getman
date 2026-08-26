using GetMan.Models;

namespace GetMan.Services;

public class ExecutionResult
{
    public PreparedRequest Request { get; set; }
    public ResponseModel Response { get; set; }
    public List<TestResult> Tests { get; } = new();
    public List<ConsoleEntry> Console { get; } = new();
    public List<VariableOp> VariableOps { get; } = new();
    public string NextRequest { get; set; }
    public bool Skipped { get; set; }
}

/// <summary>
/// Executes one request end to end: variable resolution, pre-request scripts, auth,
/// the network call and finally the test scripts - exactly the order Postman uses.
/// </summary>
public class RequestRunner
{
    private readonly HttpEngine _engine;
    private readonly ScriptRuntime _scripts = new();

    public RequestRunner(HttpEngine engine) => _engine = engine;

    public async Task<ExecutionResult> ExecuteAsync(
        CollectionNode node,
        RequestModel request,
        VariableResolver vars,
        AppSettings app,
        int iteration = 0,
        int iterationCount = 1,
        CancellationToken ct = default)
    {
        var result = new ExecutionResult();
        var prepared = RequestPreparer.Prepare(request, node, vars, app);
        result.Request = prepared;

        var ctx = new ScriptContext
        {
            Vars = vars,
            Request = prepared,
            App = app,
            Engine = _engine,
            RequestName = node?.Name ?? "Request",
            RequestId = node?.Id ?? string.Empty,
            Iteration = iteration,
            IterationCount = iterationCount
        };

        // ---- pre-request scripts: collection -> folders -> request -------------
        foreach (var script in ScriptChain(node, s => s.PreRequestScript))
            _scripts.Run(script, ScriptPhase.PreRequest, ctx, ct);
        if (!string.IsNullOrWhiteSpace(request.PreRequestScript))
            _scripts.Run(request.PreRequestScript, ScriptPhase.PreRequest, ctx, ct);

        result.Console.AddRange(ctx.Console);
        result.VariableOps.AddRange(ctx.VariableOps);
        ctx.Console.Clear();
        ctx.VariableOps.Clear();

        if (ctx.SkipRequest)
        {
            result.Skipped = true;
            result.NextRequest = ctx.NextRequest;
            return result;
        }

        // Variables created by the script now resolve inside the already-prepared request.
        ReResolve(prepared, vars);

        // ---- authorization ----------------------------------------------------
        var auth = RequestPreparer.ResolveAuthVariables(prepared.Auth, vars);
        prepared.Auth = auth;

        if (auth.Type == AuthType.OAuth2 && string.IsNullOrWhiteSpace(auth.OauthAccessToken)
            && !string.IsNullOrWhiteSpace(auth.OauthAccessTokenUrl))
        {
            var (token, refresh, raw) = await AuthApplier.FetchOAuthTokenAsync(
                auth, _engine.TokenClient, m => result.Console.Add(new ConsoleEntry { Level = "info", Message = m, Source = "OAuth2" }), ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                auth.OauthAccessToken = token;
                if (!string.IsNullOrEmpty(refresh)) auth.OauthRefreshToken = refresh;
            }
            else
            {
                result.Console.Add(new ConsoleEntry { Level = "error", Message = "OAuth2 token request failed: " + raw, Source = "OAuth2" });
            }
        }

        if (auth.Type == AuthType.NTLM)
            result.Console.Add(new ConsoleEntry { Level = "warn", Message = "NTLM auth uses the current Windows identity.", Source = "Auth" });

        AuthApplier.Apply(prepared, auth, m => result.Console.Add(new ConsoleEntry { Level = "info", Message = m }));

        // ---- send -------------------------------------------------------------
        var response = await _engine.SendAsync(prepared, app,
            m => result.Console.Add(new ConsoleEntry { Level = "info", Message = m, Source = "HTTP" }), ct).ConfigureAwait(false);
        result.Response = response;
        ctx.Response = response;

        result.Console.Add(new ConsoleEntry
        {
            Level = string.IsNullOrEmpty(response.Error) ? "info" : "error",
            Source = "HTTP",
            Message = string.IsNullOrEmpty(response.Error)
                ? $"{prepared.Method} {prepared.Url} -> {response.StatusCode} {response.StatusText} ({TextFormatter.HumanTime(response.ElapsedMs)}, {TextFormatter.HumanSize(response.SizeBytes)})"
                : $"{prepared.Method} {prepared.Url} -> {response.Error}"
        });

        // ---- test scripts -----------------------------------------------------
        if (string.IsNullOrEmpty(response.Error))
        {
            foreach (var script in ScriptChain(node, s => s.TestScript))
                _scripts.Run(script, ScriptPhase.Test, ctx, ct);
            if (!string.IsNullOrWhiteSpace(request.TestScript))
                _scripts.Run(request.TestScript, ScriptPhase.Test, ctx, ct);
        }

        result.Tests.AddRange(ctx.Tests);
        result.Console.AddRange(ctx.Console);
        result.VariableOps.AddRange(ctx.VariableOps);
        result.NextRequest = ctx.NextRequest;

        return result;
    }

    private static void ReResolve(PreparedRequest prepared, VariableResolver vars)
    {
        prepared.Url = vars.Resolve(prepared.Url);
        for (int i = 0; i < prepared.Headers.Count; i++)
        {
            var h = prepared.Headers[i];
            prepared.Headers[i] = new KeyValuePair<string, string>(vars.Resolve(h.Key), vars.Resolve(h.Value));
        }
        if (!string.IsNullOrEmpty(prepared.BodyText))
            prepared.BodyText = vars.Resolve(prepared.BodyText);
        foreach (var m in prepared.Multipart)
        {
            m.Name = vars.Resolve(m.Name);
            m.Value = vars.Resolve(m.Value);
            m.FilePath = vars.Resolve(m.FilePath);
        }
        prepared.BinaryPath = vars.Resolve(prepared.BinaryPath);
    }

    /// <summary>Collection-level script first, then each folder on the way down.</summary>
    private static IEnumerable<string> ScriptChain(CollectionNode node, Func<CollectionNode, string> selector)
    {
        if (node?.Parent == null) yield break;
        var chain = node.Parent.AncestorsAndSelf().Reverse().ToList();
        foreach (var n in chain)
        {
            var s = selector(n);
            if (!string.IsNullOrWhiteSpace(s)) yield return s;
        }
    }
}
