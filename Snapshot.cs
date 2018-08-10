using System;

namespace MyProsperity.API.Models
{
    /// <summary>
    /// This model represents a wealth snapshot for a given user.
    /// </summary>
    public class Snapshot
    {
        /// <summary>
        /// The snapshot's ID.
        /// </summary>
        public int ID { get; set; }
        /// <summary>
        /// The user's net worth at the time the snapshot was taken.
        /// </summary>
        public decimal NetWorth { get; set; }
        /// <summary>
        /// The snapshot's period.
        /// </summary>
        public DateTime Period { get; set; }
        /// <summary>
        /// The snapshot's date when it was taken.
        /// </summary>
        public DateTime DateTaken { get; set; }
    }
}