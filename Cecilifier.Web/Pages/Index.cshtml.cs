﻿using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json.Linq;

namespace Cecilifier.Web.Pages
{
    public class CecilifierApplication : PageModel
    {
        public string FromGist { get; set; } = string.Empty;
        
        public async void OnGet()
        {
            if (Request.Query.TryGetValue("gistid", out var gistid))
            {
                var gistHttp = new HttpClient();
                gistHttp.DefaultRequestHeaders.Add("User-Agent", "Cecilifier");
                var task = gistHttp.GetAsync($"https://api.github.com/gists/{gistid}");
                Task.WaitAll(task);
                
                if (task.Result.StatusCode == HttpStatusCode.OK)
                {
                    var root = JObject.Parse(await task.Result.Content.ReadAsStringAsync());
                    var source = root["files"].First().Children()["content"].FirstOrDefault().ToString();

                    FromGist = source.Replace("\n", @"\n").Replace("\t", @"\t");
                }
                else
                {
                    //TODO: How to report errors to user?
                }
            }
        }
    }
}
