using System;
using System.Collections.Generic;
using System.Threading;
using OfflineCvBioDataGenerator.Models;

namespace OfflineCvBioDataGenerator.Tests.Infrastructure
{
    /// <summary>Shared sample data so every test starts from the same known values.</summary>
    public static class TestData
    {
        /// <summary>A genuinely decodable 1x1 PNG - QuestPDF has to be able to read it.</summary>
        private const string OnePixelPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        public static byte[] OnePixelPng() => Convert.FromBase64String(OnePixelPngBase64);

        private static int _nicSequence;

        /// <summary>
        /// Builds a valid-looking, unique NIC. Clients must have a unique NIC, so tests
        /// that create more than one client would collide if the default were fixed.
        /// Pass <paramref name="nic"/> explicitly when a specific value matters.
        /// </summary>
        public static string UniqueNic()
            => $"N{Interlocked.Increment(ref _nicSequence):D8}V";

        public static Client NewClient(
            string? fullName = null,
            string? nic = null,
            string address = "12 Main Street, Colombo",
            string phone = "+94 77 123 4567",
            string email = "jane@example.com",
            DateTime? dob = null)
        {
            return new Client
            {
                FullName = fullName ?? "Jane Doe",
                NIC = nic ?? UniqueNic(),
                Address = address,
                Phone = phone,
                Email = email,
                DOB = dob,
                PhotoPath = string.Empty
            };
        }

        public static List<Education> NewEducation(int count = 1)
        {
            var list = new List<Education>();
            for (int i = 1; i <= count; i++)
            {
                list.Add(new Education
                {
                    Qualification = $"BSc Degree {i}",
                    Institute = $"University {i}",
                    Year = (2010 + i).ToString(),
                    Results = "First Class"
                });
            }
            return list;
        }

        public static List<Experience> NewExperience(int count = 1)
        {
            var list = new List<Experience>();
            for (int i = 1; i <= count; i++)
            {
                list.Add(new Experience
                {
                    JobTitle = $"Developer {i}",
                    Company = $"Company {i}",
                    Duration = $"{2015 + i} - {2016 + i}"
                });
            }
            return list;
        }

        public static ClientDetail NewDetail(Client? client = null)
        {
            return new ClientDetail
            {
                Client = client ?? NewClient(),
                EducationList = NewEducation(2),
                ExperienceList = NewExperience(2)
            };
        }
    }
}
