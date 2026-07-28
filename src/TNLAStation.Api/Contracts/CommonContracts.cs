namespace TNLAStation.Api.Contracts;

public sealed record ErrorResponse(int Code, string Message, string? Errors = null);

public sealed record VersionResponse(string Version);

/// <summary>
/// 単純な変更系 API が本文にも状態コードを載せて答えるための共通の形。EPGStation の
/// { code: 200 } 相当。
/// </summary>
public sealed record ResultCodeResponse(int Code);

/// <summary>手動予約更新のように、コードに加えてメッセージも返す EPGStation の応答に合わせる形。</summary>
public sealed record ResultMessageResponse(int Code, string Message);

/// <summary>動画アップロード成功時だけの、コードに加えて result: 'ok' を返す EPGStation の応答に合わせる形。</summary>
public sealed record UploadResultResponse(int Code, string Result);
