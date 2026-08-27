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

public enum VariableScope
{
    Global,
    Collection,
    Environment,
    Local
}

public enum TestStatus
{
    Pass,
    Fail,
    Skip
}
