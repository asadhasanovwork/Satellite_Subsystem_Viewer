using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Academy.OpsLink
{
    /// <summary>Minimal OpenC3 COSMOS Core HTTP API client (health, password, JSON-RPC).</summary>
    public sealed class Oc3Api
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        readonly string _base;
        int _id;

        public Oc3Api(string baseUrl) { _base = baseUrl.TrimEnd('/'); }

        public async Task<bool> IsUpAsync()
        {
            try { using (var r = await Http.GetAsync(_base + "/")) return r.IsSuccessStatusCode; }
            catch { return false; }
        }

        /// <summary>true/false when the API answers, null when it is not ready yet.</summary>
        public async Task<bool?> TokenExistsAsync()
        {
            try
            {
                string body = await Http.GetStringAsync(_base + "/openc3-api/auth/token-exists");
                if (body.Contains("\"result\":true")) return true;
                if (body.Contains("\"result\":false")) return false;
                return null;
            }
            catch { return null; }
        }

        public async Task<bool> SetPasswordAsync(string password)
        {
            var content = new StringContent("{\"token\":" + Json(password) + "}", Encoding.UTF8, "application/json");
            try
            {
                using (var r = await Http.PostAsync(_base + "/openc3-api/auth/set", content))
                    return r.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>Raw JSON-RPC call; returns the response body.</summary>
        public async Task<string> RpcAsync(string method, string paramsJson, string password)
        {
            string payload = "{\"jsonrpc\":\"2.0\",\"method\":" + Json(method) + ",\"params\":" + paramsJson +
                             ",\"keyword_params\":{\"scope\":\"DEFAULT\"},\"id\":" + (++_id) + "}";
            var req = new HttpRequestMessage(HttpMethod.Post, _base + "/openc3-api/api")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json-rpc"),
            };
            req.Headers.TryAddWithoutValidation("Authorization", password ?? "");
            using (var r = await Http.SendAsync(req))
                return await r.Content.ReadAsStringAsync();
        }

        public async Task<string> GetAsync(string path, string password)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, _base + path);
            req.Headers.TryAddWithoutValidation("Authorization", password ?? "");
            using (var r = await Http.SendAsync(req))
                return await r.Content.ReadAsStringAsync();
        }

        public async Task<bool> DeleteAsync(string path, string password)
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, _base + path);
            req.Headers.TryAddWithoutValidation("Authorization", password ?? "");
            using (var r = await Http.SendAsync(req))
                return r.IsSuccessStatusCode;
        }

        public async Task<string> TargetNamesAsync(string password)
        {
            try { return await RpcAsync("get_target_names", "[]", password); }
            catch (Exception e) { return "error: " + e.Message; }
        }

        public static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
