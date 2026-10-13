using System.Collections.Generic;
using Entities.Identity;

namespace ViewModels.Identity
{
    public class PagedUsersListViewModel
    {
        public PagedUsersListViewModel()
        {
            Paging = new PaginationSettings();
        }

        public List<User> Users { get; set; }

        public List<Role> Roles { get; set; }

        public PaginationSettings Paging { get; set; }
    }

    public class UsersListViewModel
    {

        public List<UserProfileViewModel> Users { get; set; }

        public int UserCount { get; set; }
    }
}
