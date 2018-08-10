using System;

namespace MyProsperity.API.Models
{
    /// <summary>
    /// This model represents a wealth item history point.
    /// </summary>
    public class WealthItemHistory
    {
        /// <summary>
        /// The history point's ID.
        /// </summary>
        public long ID { get; set; }

        /// <summary>
        /// The Wealth Item ID.
        /// </summary>

        public int WealthItem_ID { get; set; }

        /// <summary>
        /// The history point's date.
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// The given wealth item value on the given date.
        /// </summary>
        public decimal Value { get; set; }

        /// <summary>
        /// The histor's Period
        /// </summary>
        public DateTime Period { get; set; }
    }
}