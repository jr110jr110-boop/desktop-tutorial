namespace ViewModels.Identity.UserDtos
{
    /// <summary>
    /// What a picker needs about a user and nothing more (no national code, birth date, phone, e-mail or role).
    /// Every signed-in user may read the list; the full user list is for the PMO only.
    /// </summary>
    public class ResponseUserLookupDto
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string Name { get; set; }
        public string LastName { get; set; }

        /// <summary>First and last name, or the user name when both are empty.</summary>
        public string DisplayName
        {
            get
            {
                string full = $"{Name} {LastName}".Trim();
                return full.Length == 0 ? UserName : full;
            }
        }
    }
}
