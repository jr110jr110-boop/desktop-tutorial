using System;
using System.ComponentModel.DataAnnotations;

namespace ViewModels.NasbNiro.ProcessDtos
{
    public class UpdateProcessFullParametersDto : ProcessDtoBase
    {

        [Required]
        public int Id { get; set; }

        //[Required]
        //public ProcessStatus ProcessStatus { get; protected set; }

        [Required]
        public DateTimeOffset ProgramStartDate { get; set; }

        [Required]
        public DateTimeOffset ProgramEndDate { get; set; }

        [Required]
        public DateTimeOffset ActualStartDate { get; set; }

        [Required]
        public DateTimeOffset ActualEndDate { get; set; }

        [Required]
        public DateTimeOffset DocAcceptDate { get; set; }

        [Required]
        public DateTimeOffset MasterAcceptDate { get; set; }
    }
}
