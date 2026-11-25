using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Game account (Unity Authentication, username + password) used to carry the manual save
/// between PCs. Only the refreshable session token is kept on disk; the password never is.
/// </summary>
public static class CloudAccountService
{
    private const string AuthBase = "https://player-auth.services.api.unity.com/v1/authentication";
    [Serializable] private sealed class AuthResponse { public string userId; public string idToken; public string sessionToken; public int expiresIn; }
    [Serializable] private sealed class StoredSession { public int version = 1; public string username; public string userId; public string sessionToken; }

    public static string Username { get; private set; }
    public static string UserId { get; private set; }
    public static bool Busy { get; private set; }
    public static bool SignedIn => !string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(sessionToken);
    public static bool HasStoredSession => File.Exists(SessionPath);
    public static event Action Changed;

    private static string idToken, sessionToken;
    private static float idTokenExpiresAt; // Time.realtimeSinceStartup
    private static string SessionPath => Path.Combine(Application.persistentDataPath, "CloudAccount", "session.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Events are not cleared here: other classes subscribe from their own SubsystemRegistration hooks.
        Username = UserId = idToken = sessionToken = null; idTokenExpiresAt = 0; Busy = false;
        CloudRunner.Lost -= RunnerLost; CloudRunner.Lost += RunnerLost;
    }
    private static void RunnerLost() { if (Busy) { Busy = false; Changed?.Invoke(); } }

    // ---------- Input rules (mirrors the service's documented limits, checked before any request) ----------
    public static string NormalizeUsername(string raw) => (raw ?? "").Trim().ToLowerInvariant();

    public static string ValidateUsername(string username)
    {
        if (username.Length < 3 || username.Length > 20) return "아이디는 3~20자로 입력하세요.";
        foreach (char c in username)
            if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '@' || c == '_'))
                return "아이디는 영문 소문자, 숫자, . - @ _ 만 쓸 수 있습니다.";
        return null;
    }

    public static string ValidatePassword(string password)
    {
        if (password == null || password.Length < 8 || password.Length > 30) return "비밀번호는 8~30자로 입력하세요.";
        bool upper = false, lower = false, digit = false, symbol = false;
        foreach (char c in password)
        {
            if (c >= 'A' && c <= 'Z') upper = true;
            else if (c >= 'a' && c <= 'z') lower = true;
            else if (c >= '0' && c <= '9') digit = true;
            else if (!char.IsWhiteSpace(c)) symbol = true;
        }
        return upper && lower && digit && symbol ? null : "비밀번호에는 대문자, 소문자, 숫자, 특수문자가 각각 1개 이상 필요합니다.";
    }

    // ---------- Public operations (callbacks: success, Korean message) ----------
    public static void SignIn(string rawUsername, string password, Action<bool, string> done)
        => StartCredentialRequest("/usernamepassword/sign-in", rawUsername, password, false, done);

    public static void SignUp(string rawUsername, string password, Action<bool, string> done)
        => StartCredentialRequest("/usernamepassword/sign-up", rawUsername, password, true, done);

    /// <summary>Signs in again from the stored session token (same PC, no password).</summary>
    public static void ResumeStoredSession(Action<bool, string> done)
    {
        if (SignedIn) { done?.Invoke(true, null); return; }
        if (Busy) { done?.Invoke(false, "계정 요청을 처리하는 중입니다."); return; }
        if (!TryReadSession(out var stored)) { done?.Invoke(false, null); return; }
        if (!ProjectConfigured(out string configError)) { done?.Invoke(false, configError); return; }
        Busy = true; Changed?.Invoke();
        CloudRunner.Run(RefreshRoutine(stored.sessionToken, stored.username, (ok, message) =>
        {
            Busy = false;
            if (!ok && message == SessionExpiredMessage) ClearSession();
            Changed?.Invoke();
            done?.Invoke(ok, message);
        }));
    }

    public static void SignOut()
    {
        ClearSession();
        Changed?.Invoke();
    }

    /// <summary>Supplies a valid ID token, refreshing it with the session token when it is about to expire.</summary>
    internal static IEnumerator WithIdToken(Action<string, string> done)
    {
        if (!SignedIn) { done(null, "계정에 로그인되어 있지 않습니다."); yield break; }
        if (!string.IsNullOrEmpty(idToken) && Time.realtimeSinceStartup < idTokenExpiresAt - 60f) { done(idToken, null); yield break; }
        bool ok = false; string message = null;
        yield return RefreshRoutine(sessionToken, Username, (success, text) => { ok = success; message = text; });
        if (!ok && message == SessionExpiredMessage) { ClearSession(); Changed?.Invoke(); }
        done(ok ? idToken : null, ok ? null : message);
    }

    // ---------- Internals ----------
    private const string SessionExpiredMessage = "로그인이 만료되었습니다. 다시 로그인해 주세요.";

    private static bool ProjectConfigured(out string error)
    {
        error = string.IsNullOrEmpty(CloudSaveConfig.ProjectId)
            ? "Unity Cloud 프로젝트가 연결되지 않았습니다. (Project Settings > Services 확인 필요)" : null;
        return error == null;
    }

    private static void StartCredentialRequest(string path, string rawUsername, string password, bool signUp, Action<bool, string> done)
    {
        if (Busy) { done?.Invoke(false, "계정 요청을 처리하는 중입니다."); return; }
        string username = NormalizeUsername(rawUsername);
        string invalid = ValidateUsername(username) ?? (signUp ? ValidatePassword(password)
            : string.IsNullOrEmpty(password) ? "비밀번호를 입력하세요." : null);
        if (invalid != null) { done?.Invoke(false, invalid); return; }
        if (!ProjectConfigured(out string configError)) { done?.Invoke(false, configError); return; }
        Busy = true; Changed?.Invoke();
        string body = "{\"username\":\"" + CloudHttp.Escape(username) + "\",\"password\":\"" + CloudHttp.Escape(password) + "\"}";
        CloudRunner.Run(CloudHttp.Send("POST", AuthBase + path, body, null, CloudSaveConfig.ProjectId, response =>
        {
            Busy = false;
            string message = null;
            bool ok = response.Ok && Accept(response.body, username, out message);
            if (!response.Ok) message = DescribeCredentialFailure(response, signUp);
            Changed?.Invoke();
            done?.Invoke(ok, ok ? (signUp ? "계정 생성 완료 · 연동되었습니다." : "계정 연동 완료") : message);
        }));
    }

    private static IEnumerator RefreshRoutine(string token, string username, Action<bool, string> done)
    {
        string body = "{\"sessionToken\":\"" + CloudHttp.Escape(token ?? "") + "\"}";
        CloudHttp.Response result = null;
        yield return CloudHttp.Send("POST", AuthBase + "/session-token", body, null, CloudSaveConfig.ProjectId, r => result = r);
        if (result.Ok && Accept(result.body, username, out string error)) { done(true, null); yield break; }
        if (!result.Ok && (result.status == 400 || result.status == 401 || result.status == 404 && !IsProjectMissing(result)))
        { done(false, SessionExpiredMessage); yield break; }
        done(false, result.Ok ? "로그인 응답을 해석할 수 없습니다." : CloudHttp.DescribeCommonFailure(result));
    }

    private static bool IsProjectMissing(CloudHttp.Response response)
    {
        CloudHttp.ReadProblem(response, out string title, out _);
        return title == "RESOURCE_NOT_FOUND";
    }

    private static bool Accept(string json, string username, out string error)
    {
        error = null;
        AuthResponse auth;
        try { auth = JsonUtility.FromJson<AuthResponse>(json); }
        catch (Exception) { auth = null; }
        if (auth == null || string.IsNullOrEmpty(auth.userId) || string.IsNullOrEmpty(auth.idToken) || string.IsNullOrEmpty(auth.sessionToken))
        { error = "로그인 응답을 해석할 수 없습니다."; return false; }
        UserId = auth.userId; Username = username; idToken = auth.idToken; sessionToken = auth.sessionToken;
        idTokenExpiresAt = Time.realtimeSinceStartup + Mathf.Max(60, auth.expiresIn);
        // The service rotates the session token on every sign-in/refresh; always keep the newest.
        try { WriteSession(new StoredSession { username = username, userId = auth.userId, sessionToken = auth.sessionToken }); }
        catch (Exception e) { Debug.LogWarning("[Cloud Save] 로그인 세션을 이 PC에 기억하지 못했습니다: " + e.Message); }
        return true;
    }

    private static string DescribeCredentialFailure(CloudHttp.Response response, bool signUp)
    {
        if (response.networkError) return CloudHttp.DescribeCommonFailure(response);
        CloudHttp.ReadProblem(response, out string title, out string detail);
        string text = (title + " " + detail).ToLowerInvariant();
        if (signUp && (response.status == 409 || text.Contains("exist") || text.Contains("taken") || text.Contains("already")))
            return "이미 사용 중인 아이디입니다.";
        if (!signUp && (response.status == 401 || response.status == 400 && (text.Contains("password") || text.Contains("credential"))
            || response.status == 404 && title != "RESOURCE_NOT_FOUND"))
            return "아이디 또는 비밀번호가 올바르지 않습니다.";
        if (response.status == 400 && text.Contains("provider"))
            return "아이디/비밀번호 로그인이 활성화되지 않았습니다. (Unity Dashboard 설정 필요)";
        return CloudHttp.DescribeCommonFailure(response);
    }

    private static bool TryReadSession(out StoredSession stored)
    {
        stored = null;
        try
        {
            if (!File.Exists(SessionPath)) return false;
            stored = JsonUtility.FromJson<StoredSession>(File.ReadAllText(SessionPath, Encoding.UTF8));
            return stored != null && stored.version == 1 && !string.IsNullOrEmpty(stored.sessionToken);
        }
        catch (Exception) { stored = null; return false; }
    }

    private static void WriteSession(StoredSession stored)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SessionPath));
        string temporary = SessionPath + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(stored), Encoding.UTF8);
        if (File.Exists(SessionPath)) File.Replace(temporary, SessionPath, null);
        else File.Move(temporary, SessionPath);
    }

    private static void ClearSession()
    {
        Username = UserId = idToken = sessionToken = null; idTokenExpiresAt = 0;
        try
        {
            if (File.Exists(SessionPath)) File.Delete(SessionPath);
            if (File.Exists(SessionPath + ".tmp")) File.Delete(SessionPath + ".tmp");
        }
        catch (Exception e) { Debug.LogWarning("[Cloud Save] 로그인 세션 파일을 지우지 못했습니다: " + e.Message); }
    }
}
