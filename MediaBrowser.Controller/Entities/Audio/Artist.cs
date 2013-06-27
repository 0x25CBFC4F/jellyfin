﻿
namespace MediaBrowser.Controller.Entities.Audio
{
    /// <summary>
    /// Class Artist
    /// </summary>
    public class Artist : BaseItem, IItemByName
    {
        /// <summary>
        /// Gets the user data key.
        /// </summary>
        /// <returns>System.String.</returns>
        public override string GetUserDataKey()
        {
            return "Artist-" + Name;
        }

    }
}
