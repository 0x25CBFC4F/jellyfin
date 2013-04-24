﻿using MediaBrowser.Model.Entities;

namespace MediaBrowser.Model.Querying
{
    /// <summary>
    /// Class ItemsByNameQuery
    /// </summary>
    public class ItemsByNameQuery
    {
        /// <summary>
        /// Gets or sets the user id.
        /// </summary>
        /// <value>The user id.</value>
        public string UserId { get; set; }
        /// <summary>
        /// Gets or sets the start index.
        /// </summary>
        /// <value>The start index.</value>
        public int? StartIndex { get; set; }
        /// <summary>
        /// Gets or sets the size of the page.
        /// </summary>
        /// <value>The size of the page.</value>
        public int? Limit { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="ItemsByNameQuery" /> is recursive.
        /// </summary>
        /// <value><c>true</c> if recursive; otherwise, <c>false</c>.</value>
        public bool Recursive { get; set; }
        /// <summary>
        /// Gets or sets the sort order.
        /// </summary>
        /// <value>The sort order.</value>
        public SortOrder? SortOrder { get; set; }
        /// <summary>
        /// Gets or sets the parent id.
        /// </summary>
        /// <value>The parent id.</value>
        public string ParentId { get; set; }
        /// <summary>
        /// Fields to return within the items, in addition to basic information
        /// </summary>
        /// <value>The fields.</value>
        public ItemFields[] Fields { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemsByNameQuery"/> class.
        /// </summary>
        public ItemsByNameQuery()
        {
            Fields = new ItemFields[] {};
        }
    }
}
