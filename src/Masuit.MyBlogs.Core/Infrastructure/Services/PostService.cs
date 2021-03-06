﻿using Common;
using Masuit.LuceneEFCore.SearchEngine;
using Masuit.LuceneEFCore.SearchEngine.Interfaces;
using Masuit.MyBlogs.Core.Infrastructure.Application;
using Masuit.MyBlogs.Core.Infrastructure.Repository.Interface;
using Masuit.MyBlogs.Core.Infrastructure.Services.Interface;
using Masuit.MyBlogs.Core.Models.DTO;
using Masuit.MyBlogs.Core.Models.Entity;
using PanGu;
using PanGu.HighLight;
using System.Linq;

namespace Masuit.MyBlogs.Core.Infrastructure.Services
{
    public partial class PostService : BaseService<Post>, IPostService
    {
        private readonly ILuceneIndexSearcher _searcher;
        private readonly ISearchEngine<DataContext> _searchEngine;
        public PostService(IPostRepository repository, ILuceneIndexSearcher searcher, ISearchEngine<DataContext> searchEngine) : base(repository)
        {
            _searcher = searcher;
            _searchEngine = searchEngine;
        }
        public SearchResult<PostOutputDto> SearchPage(int page, int size, string keyword)
        {
            var searchResult = _searchEngine.ScoredSearch<Post>(new SearchOptions(keyword, page, size, typeof(Post)));
            var posts = searchResult.Results.Select(p => p.Entity.Mapper<PostOutputDto>()).ToList();
            var simpleHtmlFormatter = new SimpleHTMLFormatter("<span style='color:red;background-color:yellow;font-size: 1.1em;font-weight:700;'>", "</span>");
            var highlighter = new Highlighter(simpleHtmlFormatter, new Segment()) { FragmentSize = 200 };
            var keywords = _searcher.CutKeywords(keyword);
            foreach (var p in posts)
            {
                foreach (var s in keywords)
                {
                    string frag;
                    if (p.Title.Contains(s) && !string.IsNullOrEmpty(frag = highlighter.GetBestFragment(s, p.Title)))
                    {
                        p.Title = frag;
                        break;
                    }
                }
                bool handled = false;
                foreach (var s in keywords)
                {
                    string frag;
                    if (p.Content.Contains(s) && !string.IsNullOrEmpty(frag = highlighter.GetBestFragment(s, p.Content)))
                    {
                        p.Content = frag;
                        handled = true;
                        break;
                    }
                }
                if (p.Content.Length > 200 && !handled)
                {
                    p.Content = p.Content.Substring(0, 200);
                }
            }
            return new SearchResult<PostOutputDto>()
            {
                Results = posts,
                Elapsed = searchResult.Elapsed,
                Total = searchResult.TotalHits
            };
        }

        public PostService(IBaseRepository<Post> repository, ISearchEngine<DataContext> searchEngine) : base(repository)
        {
            _searchEngine = searchEngine;
        }
    }
}