using System;
using System.ComponentModel.DataAnnotations;

namespace Entities.Model
{
    public abstract class BaseEntity
    {
        [Key]
        public int Id { get; set; }
        public bool Deleted { get; protected set; }
        public DateTimeOffset CreateDate { get; set; }
        public DateTimeOffset ModifyDate { get; set; }
        public int CreatorUserId { get; set; }
        public int ModifierUserId { get; set; }

        public void Delete()
        {
            Deleted = true;
        }
    }
}
