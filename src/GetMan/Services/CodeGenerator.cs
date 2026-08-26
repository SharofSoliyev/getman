using System.Text;
using GetMan.Models;

namespace GetMan.Services;

/// <summary>Turns a prepared request into a runnable snippet for a number of languages.</summary>
public static class CodeGenerator
{
    public static readonly string[] Targets =
    {
        "cURL (bash)",
        "cURL (cmd)",
        "PowerShell",
        "C# HttpClient",
        "Python requests",
        "JavaScript fetch",
        "JavaScript axios",
        "Node.js https",
        "Go net/http",
        "Java OkHttp",
        "PHP cURL",
        "Ruby Net::HTTP",
        "Rust reqwest",
        "Dart http",
        "HTTP raw"
    };

    public static string Generate(PreparedRequest r, string target) => target switch
    {
        "cURL (bash)" => Curl(r, false),
        "cURL (cmd)" => Curl(r, true),
        "PowerShell" => PowerShell(r),
        "C# HttpClient" => CSharp(r),
        "Python requests" => Python(r),
        "JavaScript fetch" => Fetch(r),
        "JavaScript axios" => Axios(r),
        "Node.js https" => NodeHttps(r),
        "Go net/http" => Go(r),
        "Java OkHttp" => Java(r),
        "PHP cURL" => Php(r),
        "Ruby Net::HTTP" => Ruby(r),
        "Rust reqwest" => Rust(r),
        "Dart http" => Dart(r),
        "HTTP raw" => r.Dump(),
        _ => Curl(r, false)
    };

    private static IEnumerable<KeyValuePair<string, string>> AllHeaders(PreparedRequest r)
    {
        foreach (var h in r.Headers) yield return h;
        if (!r.HasHeader("Content-Type") && !string.IsNullOrEmpty(r.ContentType) && r.Mode is not (BodyMode.None or BodyMode.FormData))
            yield return new KeyValuePair<string, string>("Content-Type", r.ContentType);
    }

    private static string Body(PreparedRequest r) => r.BodyText ?? string.Empty;
    private static bool HasBody(PreparedRequest r) => r.Mode != BodyMode.None && (!string.IsNullOrEmpty(r.BodyText) || r.Multipart.Count > 0 || !string.IsNullOrEmpty(r.BinaryPath));

    private static string Sq(string s) => "'" + (s ?? string.Empty).Replace("'", "'\\''") + "'";
    private static string Dq(string s) => "\"" + (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Curl(PreparedRequest r, bool cmd)
    {
        var q = cmd ? (Func<string, string>)(s => "\"" + (s ?? string.Empty).Replace("\"", "\\\"") + "\"") : Sq;
        var cont = cmd ? " ^\n  " : " \\\n  ";
        var sb = new StringBuilder();
        sb.Append("curl -X ").Append(r.Method).Append(' ').Append(q(r.Url));
        foreach (var h in AllHeaders(r))
            sb.Append(cont).Append("-H ").Append(q($"{h.Key}: {h.Value}"));

        if (r.Mode == BodyMode.FormData)
        {
            foreach (var m in r.Multipart)
                sb.Append(cont).Append("-F ").Append(q(m.IsFile ? $"{m.Name}=@{m.FilePath}" : $"{m.Name}={m.Value}"));
        }
        else if (r.Mode == BodyMode.Binary && !string.IsNullOrEmpty(r.BinaryPath))
        {
            sb.Append(cont).Append("--data-binary ").Append(q("@" + r.BinaryPath));
        }
        else if (HasBody(r))
        {
            sb.Append(cont).Append("-d ").Append(q(Body(r)));
        }

        if (!r.Settings.VerifySsl) sb.Append(cont).Append("--insecure");
        if (r.Settings.FollowRedirects) sb.Append(cont).Append("-L");
        return sb.ToString();
    }

    private static string PowerShell(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("$headers = @{");
        foreach (var h in AllHeaders(r))
        {
            if (h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            sb.AppendLine($"    \"{h.Key}\" = \"{h.Value?.Replace("\"", "`\"")}\"");
        }
        sb.AppendLine("}");
        if (HasBody(r) && r.Mode != BodyMode.FormData)
        {
            sb.AppendLine("$body = @'");
            sb.AppendLine(Body(r));
            sb.AppendLine("'@");
        }
        sb.Append($"Invoke-RestMethod -Uri \"{r.Url}\" -Method {r.Method} -Headers $headers");
        var ct = AllHeaders(r).FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
        if (!string.IsNullOrEmpty(ct)) sb.Append($" -ContentType \"{ct}\"");
        if (HasBody(r) && r.Mode != BodyMode.FormData) sb.Append(" -Body $body");
        if (!r.Settings.VerifySsl) sb.Append(" -SkipCertificateCheck");
        return sb.ToString();
    }

    private static string CSharp(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using var client = new HttpClient();");
        sb.AppendLine($"using var request = new HttpRequestMessage(new HttpMethod(\"{r.Method}\"), {Dq(r.Url)});");
        foreach (var h in AllHeaders(r))
        {
            if (h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            sb.AppendLine($"request.Headers.TryAddWithoutValidation({Dq(h.Key)}, {Dq(h.Value)});");
        }
        if (HasBody(r) && r.Mode != BodyMode.FormData)
        {
            var ct = AllHeaders(r).FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value ?? r.ContentType;
            sb.AppendLine($"request.Content = new StringContent(@\"{Body(r).Replace("\"", "\"\"")}\", System.Text.Encoding.UTF8, {Dq(string.IsNullOrEmpty(ct) ? "text/plain" : ct.Split(';')[0])});");
        }
        else if (r.Mode == BodyMode.FormData)
        {
            sb.AppendLine("var form = new MultipartFormDataContent();");
            foreach (var m in r.Multipart)
                sb.AppendLine(m.IsFile
                    ? $"form.Add(new ByteArrayContent(File.ReadAllBytes({Dq(m.FilePath)})), {Dq(m.Name)}, Path.GetFileName({Dq(m.FilePath)}));"
                    : $"form.Add(new StringContent({Dq(m.Value)}), {Dq(m.Name)});");
            sb.AppendLine("request.Content = form;");
        }
        sb.AppendLine("var response = await client.SendAsync(request);");
        sb.AppendLine("Console.WriteLine((int)response.StatusCode);");
        sb.AppendLine("Console.WriteLine(await response.Content.ReadAsStringAsync());");
        return sb.ToString();
    }

    private static string Python(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import requests");
        sb.AppendLine();
        sb.AppendLine($"url = {Dq(r.Url)}");
        sb.AppendLine("headers = {");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"    {Dq(h.Key)}: {Dq(h.Value)},");
        sb.AppendLine("}");

        string extra = string.Empty;
        if (r.Mode == BodyMode.FormData)
        {
            sb.AppendLine("files = [");
            foreach (var m in r.Multipart.Where(m => m.IsFile))
                sb.AppendLine($"    ({Dq(m.Name)}, open({Dq(m.FilePath)}, 'rb')),");
            sb.AppendLine("]");
            sb.AppendLine("data = {");
            foreach (var m in r.Multipart.Where(m => !m.IsFile))
                sb.AppendLine($"    {Dq(m.Name)}: {Dq(m.Value)},");
            sb.AppendLine("}");
            extra = ", data=data, files=files";
        }
        else if (HasBody(r))
        {
            sb.AppendLine($"payload = \"\"\"{Body(r)}\"\"\"");
            extra = ", data=payload.encode('utf-8')";
        }

        sb.AppendLine();
        sb.AppendLine($"response = requests.request({Dq(r.Method)}, url, headers=headers{extra}{(r.Settings.VerifySsl ? "" : ", verify=False")})");
        sb.AppendLine("print(response.status_code)");
        sb.AppendLine("print(response.text)");
        return sb.ToString();
    }

    private static string Fetch(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"const response = await fetch({Dq(r.Url)}, {{");
        sb.AppendLine($"  method: {Dq(r.Method)},");
        sb.AppendLine("  headers: {");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"    {Dq(h.Key)}: {Dq(h.Value)},");
        sb.AppendLine("  },");
        if (r.Mode == BodyMode.FormData)
        {
            sb.AppendLine("  body: (() => { const f = new FormData();");
            foreach (var m in r.Multipart)
                sb.AppendLine(m.IsFile ? $"    // f.append({Dq(m.Name)}, fileInput.files[0]);" : $"    f.append({Dq(m.Name)}, {Dq(m.Value)});");
            sb.AppendLine("    return f; })(),");
        }
        else if (HasBody(r))
        {
            sb.AppendLine($"  body: {Dq(Body(r))},");
        }
        sb.AppendLine("});");
        sb.AppendLine("console.log(response.status);");
        sb.AppendLine("console.log(await response.text());");
        return sb.ToString();
    }

    private static string Axios(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const axios = require('axios');");
        sb.AppendLine();
        sb.AppendLine("const config = {");
        sb.AppendLine($"  method: {Dq(r.Method.ToLowerInvariant())},");
        sb.AppendLine($"  url: {Dq(r.Url)},");
        sb.AppendLine("  headers: {");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"    {Dq(h.Key)}: {Dq(h.Value)},");
        sb.AppendLine("  },");
        if (HasBody(r) && r.Mode != BodyMode.FormData)
            sb.AppendLine($"  data: {Dq(Body(r))},");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine("axios(config).then(r => console.log(r.status, r.data)).catch(e => console.error(e.message));");
        return sb.ToString();
    }

    private static string NodeHttps(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const https = require('https');");
        sb.AppendLine("const http = require('http');");
        sb.AppendLine($"const url = new URL({Dq(r.Url)});");
        sb.AppendLine("const options = {");
        sb.AppendLine("  hostname: url.hostname,");
        sb.AppendLine("  port: url.port || (url.protocol === 'https:' ? 443 : 80),");
        sb.AppendLine("  path: url.pathname + url.search,");
        sb.AppendLine($"  method: {Dq(r.Method)},");
        sb.AppendLine("  headers: {");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"    {Dq(h.Key)}: {Dq(h.Value)},");
        sb.AppendLine("  },");
        sb.AppendLine("};");
        sb.AppendLine("const req = (url.protocol === 'https:' ? https : http).request(options, res => {");
        sb.AppendLine("  let data = '';");
        sb.AppendLine("  res.on('data', c => data += c);");
        sb.AppendLine("  res.on('end', () => console.log(res.statusCode, data));");
        sb.AppendLine("});");
        if (HasBody(r) && r.Mode != BodyMode.FormData)
            sb.AppendLine($"req.write({Dq(Body(r))});");
        sb.AppendLine("req.end();");
        return sb.ToString();
    }

    private static string Go(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("package main");
        sb.AppendLine();
        sb.AppendLine("import (");
        sb.AppendLine("\t\"fmt\"");
        sb.AppendLine("\t\"io\"");
        sb.AppendLine("\t\"net/http\"");
        if (HasBody(r)) sb.AppendLine("\t\"strings\"");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("func main() {");
        if (HasBody(r) && r.Mode != BodyMode.FormData)
            sb.AppendLine($"\tpayload := strings.NewReader(`{Body(r)}`)");
        sb.AppendLine($"\treq, err := http.NewRequest({Dq(r.Method)}, {Dq(r.Url)}, {(HasBody(r) && r.Mode != BodyMode.FormData ? "payload" : "nil")})");
        sb.AppendLine("\tif err != nil { panic(err) }");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"\treq.Header.Add({Dq(h.Key)}, {Dq(h.Value)})");
        sb.AppendLine("\tres, err := http.DefaultClient.Do(req)");
        sb.AppendLine("\tif err != nil { panic(err) }");
        sb.AppendLine("\tdefer res.Body.Close()");
        sb.AppendLine("\tbody, _ := io.ReadAll(res.Body)");
        sb.AppendLine("\tfmt.Println(res.StatusCode)");
        sb.AppendLine("\tfmt.Println(string(body))");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Java(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("OkHttpClient client = new OkHttpClient();");
        if (HasBody(r) && r.Mode != BodyMode.FormData)
        {
            var ct = AllHeaders(r).FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value ?? r.ContentType;
            sb.AppendLine($"MediaType mediaType = MediaType.parse({Dq(string.IsNullOrEmpty(ct) ? "text/plain" : ct)});");
            sb.AppendLine($"RequestBody body = RequestBody.create(mediaType, {Dq(Body(r))});");
        }
        sb.AppendLine("Request request = new Request.Builder()");
        sb.AppendLine($"  .url({Dq(r.Url)})");
        sb.AppendLine($"  .method({Dq(r.Method)}, {(HasBody(r) && r.Mode != BodyMode.FormData ? "body" : "null")})");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"  .addHeader({Dq(h.Key)}, {Dq(h.Value)})");
        sb.AppendLine("  .build();");
        sb.AppendLine("Response response = client.newCall(request).execute();");
        sb.AppendLine("System.out.println(response.code());");
        sb.AppendLine("System.out.println(response.body().string());");
        return sb.ToString();
    }

    private static string Php(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?php");
        sb.AppendLine("$curl = curl_init();");
        sb.AppendLine("curl_setopt_array($curl, array(");
        sb.AppendLine($"  CURLOPT_URL => {Dq(r.Url)},");
        sb.AppendLine("  CURLOPT_RETURNTRANSFER => true,");
        sb.AppendLine($"  CURLOPT_CUSTOMREQUEST => {Dq(r.Method)},");
        if (HasBody(r) && r.Mode != BodyMode.FormData)
            sb.AppendLine($"  CURLOPT_POSTFIELDS => {Dq(Body(r))},");
        sb.AppendLine("  CURLOPT_HTTPHEADER => array(");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"    {Dq($"{h.Key}: {h.Value}")},");
        sb.AppendLine("  ),");
        sb.AppendLine("));");
        sb.AppendLine("$response = curl_exec($curl);");
        sb.AppendLine("curl_close($curl);");
        sb.AppendLine("echo $response;");
        return sb.ToString();
    }

    private static string Ruby(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("require 'uri'");
        sb.AppendLine("require 'net/http'");
        sb.AppendLine();
        sb.AppendLine($"url = URI({Dq(r.Url)})");
        sb.AppendLine("http = Net::HTTP.new(url.host, url.port)");
        sb.AppendLine("http.use_ssl = url.scheme == 'https'");
        sb.AppendLine($"request = Net::HTTP::{Capitalize(r.Method)}.new(url)");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"request[{Dq(h.Key)}] = {Dq(h.Value)}");
        if (HasBody(r) && r.Mode != BodyMode.FormData)
            sb.AppendLine($"request.body = {Dq(Body(r))}");
        sb.AppendLine("response = http.request(request)");
        sb.AppendLine("puts response.code");
        sb.AppendLine("puts response.read_body");
        return sb.ToString();
    }

    private static string Rust(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("use reqwest::header::{HeaderMap, HeaderName, HeaderValue};");
        sb.AppendLine();
        sb.AppendLine("#[tokio::main]");
        sb.AppendLine("async fn main() -> Result<(), Box<dyn std::error::Error>> {");
        sb.AppendLine("    let mut headers = HeaderMap::new();");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"    headers.insert(HeaderName::from_static({Dq(h.Key.ToLowerInvariant())}), HeaderValue::from_static({Dq(h.Value)}));");
        sb.AppendLine("    let client = reqwest::Client::new();");
        sb.Append($"    let res = client.request(reqwest::Method::from_bytes(b{Dq(r.Method)})?, {Dq(r.Url)}).headers(headers)");
        if (HasBody(r) && r.Mode != BodyMode.FormData) sb.Append($".body({Dq(Body(r))})");
        sb.AppendLine(".send().await?;");
        sb.AppendLine("    println!(\"{}\", res.status());");
        sb.AppendLine("    println!(\"{}\", res.text().await?);");
        sb.AppendLine("    Ok(())");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Dart(PreparedRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import 'package:http/http.dart' as http;");
        sb.AppendLine();
        sb.AppendLine("void main() async {");
        sb.AppendLine("  final headers = {");
        foreach (var h in AllHeaders(r))
            sb.AppendLine($"    {Dq(h.Key)}: {Dq(h.Value)},");
        sb.AppendLine("  };");
        sb.AppendLine($"  final request = http.Request({Dq(r.Method)}, Uri.parse({Dq(r.Url)}));");
        sb.AppendLine("  request.headers.addAll(headers);");
        if (HasBody(r) && r.Mode != BodyMode.FormData)
            sb.AppendLine($"  request.body = {Dq(Body(r))};");
        sb.AppendLine("  final response = await request.send();");
        sb.AppendLine("  print(response.statusCode);");
        sb.AppendLine("  print(await response.stream.bytesToString());");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Capitalize(string m) =>
        string.IsNullOrEmpty(m) ? "Get" : char.ToUpperInvariant(m[0]) + m.Substring(1).ToLowerInvariant();
}
