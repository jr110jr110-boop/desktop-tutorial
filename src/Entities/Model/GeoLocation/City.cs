using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Entities.Model
{
    public class City
    {
        public City(int id, string name, int addressId)
        {
            Update(id, name, addressId);
        }

        public void Update(int id, string name, int addressId)
        {
            Id = id;
            Name = name;
            AddressId = addressId;
        }


        [Key]
        public int KeyId { get; set; }

        public int Id { get; set; }
        public string Name { get; set; }

        public int AddressId { get; set; }
        [ForeignKey("AddressId")]
        public virtual Address Address { get; set; }



    }
}
