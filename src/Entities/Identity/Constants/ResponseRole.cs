namespace Entities.Identity.Constants
{
    public class ResponseRole
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public static implicit operator string(ResponseRole responseRole)
        {
            return responseRole.Name;
        }
    }
}
