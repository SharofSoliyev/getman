namespace GetMan.Models;

public enum NodeKind
{
    Collection,
    Folder,
    Request
}

public enum BodyMode
{
    None,
    Raw,
    FormData,
    UrlEncoded,
    Binary,
    GraphQL
}

public enum AuthType
{
    Inherit,
    None,
    Bearer,
    Basic,
    ApiKey,
    OAuth2,
    Digest,
    NTLM,
    AwsV4,
    Hawk
}

public enum RequestProtocol
{
    Http,
    WebSocket,
    Sse
}

public enum ParamKind
{
    Text,
    File
}

/// <summary>Where a variable came from, in the resolver's precedence order: nearest wins.</summary>
public enum VariableScope
{
    Local,
    Data,
    Environment,
    Collection,
    Global,
    /// <summary>A {{$generator}} rather than a stored value.</summary>
    Dynamic
}

public enum TestStatus
{
    Pass,
    Fail,
    Skip
}
