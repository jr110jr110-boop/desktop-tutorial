using System;
using System.Collections.Generic;
using ViewModels.Identity.UserDtos;

namespace ViewModels.NasbNiro
{
    public class ResponseDashboardUserDto
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public Guid? ProfilePhotoId { get; set; }
        public IEnumerable<ResponseUserRoleDto> Roles { get; set; }
    }
}