namespace ViewModels
{

    public class DataTableAjaxPostModel
    {
        public int pageNumber { get; set; }
        public int pageSize { get; set; }
        public string search { get; set; }
        public string column { get; set; }
        public string dir { get; set; }

        public string filterBy { get; set; }
        public string filterValue { get; set; }

    }
}
