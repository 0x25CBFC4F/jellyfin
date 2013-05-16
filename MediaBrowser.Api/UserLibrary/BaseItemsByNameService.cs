﻿using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using ServiceStack.ServiceHost;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MediaBrowser.Api.UserLibrary
{
    /// <summary>
    /// Class BaseItemsByNameService
    /// </summary>
    /// <typeparam name="TItemType">The type of the T item type.</typeparam>
    public abstract class BaseItemsByNameService<TItemType> : BaseApiService
        where TItemType : BaseItem
    {
        /// <summary>
        /// The _user manager
        /// </summary>
        protected readonly IUserManager UserManager;
        /// <summary>
        /// The library manager
        /// </summary>
        protected readonly ILibraryManager LibraryManager;
        protected readonly IUserDataRepository UserDataRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseItemsByNameService{TItemType}" /> class.
        /// </summary>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="userDataRepository">The user data repository.</param>
        protected BaseItemsByNameService(IUserManager userManager, ILibraryManager libraryManager, IUserDataRepository userDataRepository)
        {
            UserManager = userManager;
            LibraryManager = libraryManager;
            UserDataRepository = userDataRepository;
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Task{ItemsResult}.</returns>
        protected async Task<ItemsResult> GetResult(GetItemsByName request)
        {
            User user = null;
            BaseItem item;

            if (request.UserId.HasValue)
            {
                user = UserManager.GetUserById(request.UserId.Value);
                item = string.IsNullOrEmpty(request.ParentId) ? user.RootFolder : DtoBuilder.GetItemByClientId(request.ParentId, UserManager, LibraryManager, user.Id);
            }
            else
            {
                item = string.IsNullOrEmpty(request.ParentId) ? LibraryManager.RootFolder : DtoBuilder.GetItemByClientId(request.ParentId, UserManager, LibraryManager);
            }

            IEnumerable<BaseItem> items;

            if (item.IsFolder)
            {
                var folder = (Folder)item;

                if (request.UserId.HasValue)
                {
                    items = request.Recursive ? folder.GetRecursiveChildren(user) : folder.GetChildren(user);
                }
                else
                {
                    items = request.Recursive ? folder.RecursiveChildren: folder.Children;
                }
            }
            else
            {
                items = new[] { item };
            }

            items = FilterItems(request, items);

            var extractedItems = GetAllItems(request, items);

            extractedItems = FilterItems(request, extractedItems, user);
            extractedItems = SortItems(request, extractedItems);

            var ibnItemsArray = extractedItems.ToArray();

            IEnumerable<IbnStub<TItemType>> ibnItems = ibnItemsArray;

            var result = new ItemsResult
            {
                TotalRecordCount = ibnItemsArray.Length
            };

            if (request.StartIndex.HasValue || request.Limit.HasValue)
            {
                if (request.StartIndex.HasValue)
                {
                    ibnItems = ibnItems.Skip(request.StartIndex.Value);
                }

                if (request.Limit.HasValue)
                {
                    ibnItems = ibnItems.Take(request.Limit.Value);
                }

            }

            var fields = request.GetItemFields().ToList();

            var tasks = ibnItems.Select(i => GetDto(i, user, fields));

            var resultItems = await Task.WhenAll(tasks).ConfigureAwait(false);

            result.Items = resultItems.Where(i => i != null).ToArray();

            return result;
        }

        /// <summary>
        /// Filters the items.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="items">The items.</param>
        /// <param name="user">The user.</param>
        /// <returns>IEnumerable{IbnStub}.</returns>
        private IEnumerable<IbnStub<TItemType>> FilterItems(GetItemsByName request, IEnumerable<IbnStub<TItemType>> items, User user)
        {
            if (!string.IsNullOrEmpty(request.NameStartsWith))
            {
                items = items.Where(i => i.Name.IndexOf(request.NameStartsWith, StringComparison.OrdinalIgnoreCase) == 0);
            }

            var filters = request.GetFilters().ToList();

            if (filters.Count == 0)
            {
                return items;
            }

            items = items.AsParallel();

            if (filters.Contains(ItemFilter.Dislikes))
            {
                items = items.Where(i =>
                {
                    var userdata = i.GetUserItemData(UserDataRepository, user.Id).Result;

                    return userdata != null && userdata.Likes.HasValue && !userdata.Likes.Value;
                });
            }

            if (filters.Contains(ItemFilter.Likes))
            {
                items = items.Where(i =>
                {
                    var userdata = i.GetUserItemData(UserDataRepository, user.Id).Result;

                    return userdata != null && userdata.Likes.HasValue && userdata.Likes.Value;
                });
            }

            if (filters.Contains(ItemFilter.IsFavorite))
            {
                items = items.Where(i =>
                {
                    var userdata = i.GetUserItemData(UserDataRepository, user.Id).Result;

                    return userdata != null && userdata.Likes.HasValue && userdata.IsFavorite;
                });
            }
            
            return items.AsEnumerable();
        }
        
        /// <summary>
        /// Sorts the items.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="items">The items.</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        private IEnumerable<IbnStub<TItemType>> SortItems(GetItemsByName request, IEnumerable<IbnStub<TItemType>> items)
        {
            if (string.Equals(request.SortBy, "SortName", StringComparison.OrdinalIgnoreCase))
            {
                if (request.SortOrder.HasValue && request.SortOrder.Value == Model.Entities.SortOrder.Descending)
                {
                    items = items.OrderByDescending(i => i.Name);
                }
                else
                {
                    items = items.OrderBy(i => i.Name);
                }
            }

            return items;
        }

        /// <summary>
        /// Filters the items.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="items">The items.</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        protected virtual IEnumerable<BaseItem> FilterItems(GetItemsByName request, IEnumerable<BaseItem> items)
        {
            // Exclude item types
            if (!string.IsNullOrEmpty(request.ExcludeItemTypes))
            {
                var vals = request.ExcludeItemTypes.Split(',');
                items = items.Where(f => !vals.Contains(f.GetType().Name, StringComparer.OrdinalIgnoreCase));
            }

            // Include item types
            if (!string.IsNullOrEmpty(request.IncludeItemTypes))
            {
                var vals = request.IncludeItemTypes.Split(',');
                items = items.Where(f => vals.Contains(f.GetType().Name, StringComparer.OrdinalIgnoreCase));
            }

            // Include MediaTypes
            if (!string.IsNullOrEmpty(request.MediaTypes))
            {
                var vals = request.MediaTypes.Split(',');

                items = items.Where(f => vals.Contains(f.MediaType ?? string.Empty, StringComparer.OrdinalIgnoreCase));
            }
            
            return items;
        }

        /// <summary>
        /// Gets all items.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="items">The items.</param>
        /// <returns>IEnumerable{Tuple{System.StringFunc{System.Int32}}}.</returns>
        protected abstract IEnumerable<IbnStub<TItemType>> GetAllItems(GetItemsByName request, IEnumerable<BaseItem> items);

        /// <summary>
        /// Gets the dto.
        /// </summary>
        /// <param name="stub">The stub.</param>
        /// <param name="user">The user.</param>
        /// <param name="fields">The fields.</param>
        /// <returns>Task{DtoBaseItem}.</returns>
        private async Task<BaseItemDto> GetDto(IbnStub<TItemType> stub, User user, List<ItemFields> fields)
        {
            BaseItem item;

            try
            {
                item = await stub.GetItem().ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                Logger.ErrorException("Error getting IBN item {0}", ex, stub.Name);
                return null;
            }

            var dto = user == null ? await new DtoBuilder(Logger, LibraryManager, UserDataRepository).GetBaseItemDto(item, fields).ConfigureAwait(false) :
                await new DtoBuilder(Logger, LibraryManager, UserDataRepository).GetBaseItemDto(item, fields, user).ConfigureAwait(false);

            if (fields.Contains(ItemFields.ItemCounts))
            {
                var items = stub.Items;

                dto.ChildCount = items.Count;
                dto.RecentlyAddedItemCount = items.Count(i => i.IsRecentlyAdded());
            }

            return dto;
        }

        /// <summary>
        /// Gets the items.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns>IEnumerable{BaseItem}.</returns>
        protected IEnumerable<BaseItem> GetItems(Guid? userId)
        {
            if (userId.HasValue)
            {
                var user = UserManager.GetUserById(userId.Value);

                return UserManager.GetUserById(userId.Value).RootFolder.GetRecursiveChildren(user);
            }

            return LibraryManager.RootFolder.RecursiveChildren;
        }
    }

    /// <summary>
    /// Class GetItemsByName
    /// </summary>
    public class GetItemsByName : BaseItemsRequest, IReturn<ItemsResult>
    {
        /// <summary>
        /// Gets or sets the user id.
        /// </summary>
        /// <value>The user id.</value>
        [ApiMember(Name = "UserId", Description = "Optional. Filter by user id, and attach user data", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public Guid? UserId { get; set; }

        [ApiMember(Name = "NameStartsWith", Description = "Optional filter whose name begins with a prefix.", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public string NameStartsWith { get; set; }
        
        /// <summary>
        /// What to sort the results by
        /// </summary>
        /// <value>The sort by.</value>
        [ApiMember(Name = "SortBy", Description = "Optional. Options: SortName", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET", AllowMultiple = true)]
        public string SortBy { get; set; }

        public GetItemsByName()
        {
            Recursive = true;
        }
    }

    public class IbnStub<T>
        where T : BaseItem
    {
        private readonly Func<IEnumerable<BaseItem>> _childItemsFunction;
        private List<BaseItem> _childItems;

        private readonly Func<string,Task<T>> _itemFunction;
        private Task<T> _itemTask;
        
        public string Name;

        public BaseItem Item;
        private Task<UserItemData> _userData;

        public List<BaseItem> Items
        {
            get { return _childItems ?? (_childItems = _childItemsFunction().ToList()); }
        }

        public Task<T> GetItem()
        {
            return _itemTask ?? (_itemTask = _itemFunction(Name));
        }

        public async Task<UserItemData> GetUserItemData(IUserDataRepository repo, Guid userId)
        {
            var item = await GetItem().ConfigureAwait(false);

            if (_userData == null)
            {
                _userData = repo.GetUserData(userId, item.GetUserDataKey());
            }

            return await _userData.ConfigureAwait(false);
        }

        public IbnStub(string name, Func<IEnumerable<BaseItem>> childItems, Func<string,Task<T>> item)
        {
            Name = name;
            _childItemsFunction = childItems;
            _itemFunction = item;
        }
    }
}
