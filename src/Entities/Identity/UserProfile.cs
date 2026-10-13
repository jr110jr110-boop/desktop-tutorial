using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Entities.Model;

namespace Entities.Identity
{
    public class UserProfile
    {
        protected UserProfile()
        {

        }

        public UserProfile(int userId, string fatherName, bool gender, string seriShChar, string seriSh, string serial, string shNumber,
            DateTime? birthDate, string placeOfIssue, string placeOfBirth, string signatureFile, bool createdByAdmin, string inviteCode, int? inviterUserId,
            string nationalCode, string type, string status, string agent, string address, long personageId, long branchId, string branchName,
            long? groupId) : this()
        {
            Update(userId, fatherName, gender, seriShChar, seriSh, serial, shNumber, birthDate, placeOfIssue, placeOfBirth, signatureFile,
                createdByAdmin, inviteCode, inviterUserId, nationalCode, type, status, agent, address, personageId, branchId, branchName, groupId);
        }

        public void Update(int userId, string fatherName, bool gender, string seriShChar, string seriSh, string serial, string shNumber, DateTime? birthDate,
            string placeOfIssue, string placeOfBirth, string signatureFile, bool createdByAdmin, string inviteCode, int? inviterUserId,
            string nationalCode, string type, string status, string agent, string address, long personageId, long branchId, string branchName,
            long? groupId)
        {
            UserId = userId;
            FatherName = fatherName;
            Gender = gender;
            SeriShChar = seriShChar;
            SeriSh = seriSh;
            Serial = serial;
            ShNumber = shNumber;
            BirthDate = birthDate;
            PlaceOfIssue = placeOfIssue;
            PlaceOfBirth = placeOfBirth;
            SignatureFile = signatureFile;
            CreatedByAdmin = createdByAdmin;
            InviteCode = inviteCode;
            InviterUserId = inviterUserId;
            NationalCode = nationalCode;
            Type = type;
            Status = status;
            Agent = agent;
            GeoAddress = address;
            PersonageId = personageId;
            BranchId = branchId;
            BranchName = branchName;
            GroupId = groupId;
        }


        public void UpdateSimpleData(string fatherName, string nationalCode, long customerId, long personageId,
            long branchId, string branchName, long? groupId)
        {
            if (!String.IsNullOrWhiteSpace(fatherName))
            {
                FatherName = fatherName;
            }

            if (!String.IsNullOrWhiteSpace(nationalCode))
            {
                NationalCode = nationalCode;
            }

            if (customerId > 0)
            {
                CustomerId = customerId;
            }

            if (personageId > 0)
            {
                PersonageId = personageId;
            }

            if (branchId > 0)
            {
                BranchId = branchId;
            }

            if (!String.IsNullOrWhiteSpace(branchName))
            {
                BranchName = branchName;
            }

            if (groupId != null && groupId > 0)
            {
                GroupId = groupId;
            }
        }

        public void ApprovedUser(bool isApproved)
        {
            this.IsApproved = isApproved;
        }

        public void SetCustomerId(long customerId)
        {
            this.CustomerId = customerId;
        }

        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }
        public string FatherName { get; set; }

        public bool Gender { get; set; }

        public string NationalCode { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string Agent { get; set; }

        public string SeriShChar { get; set; }

        public string GeoAddress { get; set; }

        public string SeriSh { get; set; }

        public string Serial { get; set; }

        public string ShNumber { get; set; }

        public DateTime? BirthDate { get; set; }

        public string PlaceOfIssue { get; set; }

        public string PlaceOfBirth { get; set; }

        public string SignatureFile { get; set; }

        public bool CreatedByAdmin { get; set; }

        public string InviteCode { get; set; }

        public bool IsApproved { get; set; }

        public long CustomerId { get; set; }

        public long PersonageId { get; set; }

        public long BranchId { get; set; }

        public string BranchName { get; set; }

        public int? InviterUserId { get; set; }
        [ForeignKey("InviterUserId")]
        public virtual User InviterUser { get; set; }

        public long? GroupId { get; set; }

        public virtual ICollection<Address> Addresses { get; set; }

    }
}
