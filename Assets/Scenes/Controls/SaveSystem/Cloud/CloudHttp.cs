using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Minimal JSON-over-HTTPS helper for the Unity Gaming Services REST APIs (no extra packages).</summary>
internal static class CloudHttp
{
    public sealed class Response
    {
        public long status;
        public string body = "";
        public bool networkError;
        public string transportError;
        public string retryAfter;
        public bool Ok => !networkError && status >= 200 && status < 300;
    }

    // RFC 7807 style error body returned by the auth and Cloud Save services.
    [Serializable] private sealed class Problem { public int status; public string title; public string detail; }

    public static IEnumerator Send(string method, string url, string jsonBody, string bearerToken, string projectId,
        Action<Response> done)
    {
        var response = new Response();
        using (var request = new UnityWebRequest(url, method))
        {
            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(5, CloudSaveConfig.Current.requestTimeoutSeconds);
            if (!string.IsNullOrEmpty(projectId)) request.SetRequestHeader("ProjectId", projectId);
            if (!string.IsNullOrEmpty(bearerToken)) request.SetRequestHeader("Authorization", "Bearer " + bearerToken);
            yield return request.SendWebRequest();

            response.status = request.responseCode;
            response.body = request.downloadHandler != null ? request.downloadHandler.text ?? "" : "";
            response.retryAfter = request.GetResponseHeader("Retry-After");
            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                response.networkError = true;
                response.transportError = request.error;
            }
        }
        done(response);
    }

    /// <summary>Returns (title, detail) from an error body, or empty strings if it is not a problem document.</summary>
    public static void ReadProblem(Response response, out string title, out string detail)
    {
        title = detail = "";
        if (response == null || string.IsNullOrEmpty(response.body) || response.body[0] != '{') return;
        try
        {
            var problem = JsonUtility.FromJson<Problem>(response.body);
            title = problem?.title ?? ""; detail = problem?.detail ?? "";
        }
        catch (Exception) { /* Non-JSON error page: callers fall back to the status code. */ }
    }

    /// <summary>Player-facing Korean text for failures shared by every endpoint.</summary>
    public static string DescribeCommonFailure(Response response)
    {
        if (response.networkError) return "서버에 연결할 수 없습니다. 인터넷 연결을 확인하세요.";
        ReadProblem(response, out string title, out string detail);
        switch (response.status)
        {
            case 403 when title == "BANNED_USER": return "이용이 제한된 계정입니다.";
            case 404 when title == "RESOURCE_NOT_FOUND":
                return "Unity Cloud 프로젝트 연결 또는 서비스 설정을 찾을 수 없습니다. (개발자 설정 확인 필요)";
            case 429: return "요청이 너무 많습니다. " + (string.IsNullOrEmpty(response.retryAfter) ? "잠시 후" : response.retryAfter + "초 후") + " 다시 시도하세요.";
            case 500: case 502: case 503: case 504: return "클라우드 서버가 일시적으로 응답하지 않습니다. 잠시 후 다시 시도하세요.";
        }
        string suffix = detail.Length > 0 ? detail : title;
        return $"요청 실패 ({response.status}){(suffix.Length > 0 ? ": " + suffix : "")}";
    }

    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                    else builder.Append(c);
                    break;
            }
        }
        return builder.ToString();
    }
}
