using System.IO;
using System.Linq;
using System.Net;
using System.Security.RightsManagement;
using Newtonsoft.Json.Linq;
using SpsLogic;

namespace SpsGui.Models.Services
{
    public interface IVersionCheckService
    {
        bool FetchLatest();
        string GetVersion();
        JObject GetLatestRelease();
        string GetRepoName();
    }

    public class VersionCheckService : IVersionCheckService
    {
        private static readonly bool IgnoreLatestForDebug = false;
        private static readonly string CurrentVersion = "1.1(Test)";
        private static readonly string repositoryName = "Pepton-0/SteamP2PScanner";
        private static JObject LatestRelease;

        public bool FetchLatest()
        {
            if (IgnoreLatestForDebug)
            {
                return false;
            }

            string query = "https://api.github.com/repos/" + repositoryName + "/releases";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(query);
            req.UserAgent = "request";
            HttpWebResponse resp;

            try
            {
                resp = (HttpWebResponse)req.GetResponse();
            }
            catch (WebException)
            {
                LatestRelease = null;
                return false;
            }

            if (resp.StatusCode != HttpStatusCode.OK)
            {
                LatestRelease = null;
                return false;
            }

            using (StreamReader reader = new StreamReader(resp.GetResponseStream()))
            {
                JArray data = JArray.Parse(reader.ReadToEnd());
                if (data.Count == 0)
                {
                    LatestRelease = null;
                    return false;
                }

                var releases = data.Where(r => !(bool)r["prerelease"]);
                bool existsReleases = releases.Any();
                if (existsReleases)
                {
                    LatestRelease = (JObject)releases.First();
                }
                return existsReleases;
            }
        }

        public string GetVersion()
        {
            return CurrentVersion;
        }

        public JObject GetLatestRelease()
        {
            return LatestRelease;
        }

        public string GetRepoName()
        {
            return repositoryName;
        }
    }
}