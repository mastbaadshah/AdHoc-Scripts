using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Data.Enumerations.Notifications;

namespace Data.Model
{
    public class NotificationTemplate
    {
        [Key]
        public int ID { get; set; }

        [Required]
        public string TemplateName { get; set; }

        public string Description { get; set; }

        [Required]
        public string TemplateBody { get; set; }

        [NotMapped]
        public MessageType MessageType
        {
            get { return (MessageType)MessageTypeInternal; }
            set { MessageTypeInternal = (int)value; }
        }

        public int MessageTypeInternal { get; set; }

        [NotMapped]
        public FormatType FormatType
        {
            get { return (FormatType)FormatTypeInternal; }
            set { FormatTypeInternal = (int)value; }
        }

        [Required]
        public int FormatTypeInternal { get; set; }

        [Required]
        public DateTime DateCreated { get; set; }

        [Required]
        public int TemplatePlaceholderStrategy { get; set; }

        [NotMapped]
        public TemplatePlaceholderStrategy TemplatePlaceholderStrategyEnum
        {
            get { return (TemplatePlaceholderStrategy)TemplatePlaceholderStrategy; }
            set { TemplatePlaceholderStrategy = (int)value; }
        }

        public string SubjectTitle { get; set; }
    }
}
