﻿using Common;
using Masuit.MyBlogs.Core.Infrastructure.Services.Interface;
using Masuit.MyBlogs.Core.Models.DTO;
using Masuit.MyBlogs.Core.Models.Entity;
using Masuit.MyBlogs.Core.Models.Enum;
using Masuit.Tools.Core.Net;
using Masuit.Tools.NoSQL;
using Masuit.Tools.Systems;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Masuit.MyBlogs.Core.Extensions.Hangfire
{
    public class HangfireBackJob : IHangfireBackJob
    {
        private readonly IUserInfoService _userInfoService;
        private readonly IPostService _postService;
        private readonly ISystemSettingService _settingService;
        private readonly ISearchDetailsService _searchDetailsService;
        private readonly ILinksService _linksService;
        private readonly RedisHelper _redisHelper;
        private readonly IHttpClientFactory _httpClientFactory;

        public HangfireBackJob(IUserInfoService userInfoService, IPostService postService, ISystemSettingService settingService, ISearchDetailsService searchDetailsService, ILinksService linksService, RedisHelper redisHelper, IHttpClientFactory httpClientFactory)
        {
            _userInfoService = userInfoService;
            _postService = postService;
            _settingService = settingService;
            _searchDetailsService = searchDetailsService;
            _linksService = linksService;
            _redisHelper = redisHelper;
            _httpClientFactory = httpClientFactory;
        }

        public void LoginRecord(UserInfoOutputDto userInfo, string ip, LoginType type)
        {
            var result = ip.GetPhysicsAddressInfo().Result;
            if (result?.Status == 0)
            {
                string addr = result.AddressResult.FormattedAddress;
                string prov = result.AddressResult.AddressComponent.Province;
                LoginRecord record = new LoginRecord()
                {
                    IP = ip,
                    LoginTime = DateTime.Now,
                    LoginType = type,
                    PhysicAddress = addr,
                    Province = prov
                };
                UserInfo u = _userInfoService.GetByUsername(userInfo.Username);
                u.LoginRecord.Add(record);
                _userInfoService.UpdateEntitySaved(u);
                string content = File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "template\\login.html").Replace("{{name}}", u.Username).Replace("{{time}}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Replace("{{ip}}", record.IP).Replace("{{address}}", record.PhysicAddress);
                CommonHelper.SendMail(_settingService.GetFirstEntity(s => s.Name.Equals("Title")).Value + "账号登录通知", content, _settingService.GetFirstEntity(s => s.Name.Equals("ReceiveEmail")).Value);
            }
        }

        public void PublishPost(Post p)
        {
            p.Status = Status.Pended;
            p.PostDate = DateTime.Now;
            p.ModifyDate = DateTime.Now;
            Post post = _postService.GetById(p.Id);
            if (post is null)
            {
                _postService.AddEntitySaved(post);
            }
            else
            {
                post.Status = Status.Pended;
                post.PostDate = DateTime.Now;
                post.ModifyDate = DateTime.Now;
                _postService.UpdateEntitySaved(post);
            }
        }

        public void RecordPostVisit(int pid)
        {
            Post post = _postService.GetById(pid);
            var record = post.PostAccessRecord.FirstOrDefault(r => r.AccessTime == DateTime.Today);
            if (record != null)
            {
                record.ClickCount += 1;
            }
            else
            {
                post.PostAccessRecord.Add(new PostAccessRecord
                {
                    ClickCount = 1,
                    AccessTime = DateTime.Today
                });
            }

            _postService.UpdateEntitySaved(post);
        }

        public static void InterceptLog(IpIntercepter s)
        {
            using (RedisHelper redisHelper = RedisHelper.GetInstance())
            {
                redisHelper.StringIncrement("interceptCount");
                redisHelper.ListLeftPush("intercept", s);
            }
        }

        /// <summary>
        /// 每天的任务
        /// </summary>
        public void EverydayJob()
        {
            CommonHelper.IPErrorTimes.RemoveWhere(kv => kv.Value < 100); //将访客访问出错次数少于100的移开
            _redisHelper.SetString("ArticleViewToken", SnowFlake.GetInstance().GetUniqueShortId(6)); //更新加密文章的密码
            _redisHelper.StringIncrement("Interview:RunningDays");
            DateTime time = DateTime.Now.AddMonths(-1);
            _searchDetailsService.DeleteEntitySaved(s => s.SearchTime < time);
            foreach (var p in _postService.GetAll().AsParallel())
            {
                try
                {
                    p.AverageViewCount = p.PostAccessRecord.Average(r => r.ClickCount);
                    p.TotalViewCount = p.PostAccessRecord.Sum(r => r.ClickCount);
                    _postService.UpdateEntitySaved(p);
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// 检查友链
        /// </summary>
        public void CheckLinks()
        {
            var links = _linksService.LoadEntities(l => !l.Except).AsParallel();
            Parallel.ForEach(links, link =>
            {
                Uri uri = new Uri(link.Url);
                HttpClient client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse("Mozilla/5.0"));
                client.DefaultRequestHeaders.Referrer = new Uri("https://masuit.com");
                client.Timeout = TimeSpan.FromHours(10);
                client.GetAsync(uri).ContinueWith(async t =>
                {
                    if (t.IsCanceled || t.IsFaulted)
                    {
                        link.Status = Status.Unavailable;
                        return;
                    }
                    var res = await t;
                    if (res.IsSuccessStatusCode)
                    {
                        link.Status = !(await res.Content.ReadAsStringAsync()).Contains(CommonHelper.SystemSettings["Domain"]) ? Status.Unavailable : Status.Available;
                    }
                    else
                    {
                        link.Status = Status.Unavailable;
                    }
                    _linksService.UpdateEntity(link);
                }).Wait();
            });
            _linksService.SaveChanges();
        }
    }
}