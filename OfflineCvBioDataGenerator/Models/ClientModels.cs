using System;

namespace OfflineCvBioDataGenerator.Models
{
    public class Client
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime? DOB { get; set; }
        public string NIC { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
    }

    public class Education
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public string Qualification { get; set; } = string.Empty;
        public string Institute { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public string Results { get; set; } = string.Empty;
    }

    public class Experience
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public string JobTitle { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
    }

    public class ClientDetail
    {
        public Client Client { get; set; } = new();
        public List<Education> EducationList { get; set; } = new();
        public List<Experience> ExperienceList { get; set; } = new();
    }
}
