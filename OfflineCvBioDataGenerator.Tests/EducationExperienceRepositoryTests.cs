using System;
using System.Collections.Generic;
using System.Linq;
using OfflineCvBioDataGenerator.Models;
using OfflineCvBioDataGenerator.Repositories;
using OfflineCvBioDataGenerator.Tests.Infrastructure;
using Xunit;

namespace OfflineCvBioDataGenerator.Tests
{
    public class EducationRepositoryTests : IDisposable
    {
        private readonly TempDatabase _db = new();
        private readonly ClientRepository _clientRepo = new();
        private readonly EducationRepository _eduRepo = new();

        public void Dispose() => _db.Dispose();

        private int NewClientId(string nic = "900000000V") =>
            _clientRepo.AddClient(TestData.NewClient(nic: nic));

        [Fact]
        public void Add_ReturnsIdAndRoundTripsEveryColumn()
        {
            int clientId = NewClientId();

            int id = _eduRepo.Add(new Education
            {
                ClientId = clientId,
                Qualification = "BSc Computer Science",
                Institute = "University of Colombo",
                Year = "2014",
                Results = "GPA 3.7"
            });

            Assert.True(id > 0);

            Education saved = _eduRepo.GetByClientId(clientId).Single();
            Assert.Equal("BSc Computer Science", saved.Qualification);
            Assert.Equal("University of Colombo", saved.Institute);
            Assert.Equal("2014", saved.Year);
            Assert.Equal("GPA 3.7", saved.Results);
        }

        [Fact]
        public void GetByClientId_OnlyReturnsThatClientsRows()
        {
            int first = NewClientId("111111111V");
            int second = NewClientId("222222222V");
            _eduRepo.AddRange(TestData.NewEducation(2).Select(e => { e.ClientId = first; return e; }));
            _eduRepo.AddRange(TestData.NewEducation(3).Select(e => { e.ClientId = second; return e; }));

            Assert.Equal(2, _eduRepo.GetByClientId(first).Count);
            Assert.Equal(3, _eduRepo.GetByClientId(second).Count);
        }

        [Fact]
        public void GetByClientId_UnknownClient_ReturnsEmptyList()
        {
            Assert.Empty(_eduRepo.GetByClientId(424242));
        }

        [Fact]
        public void AddRange_EmptyOrNull_IsANoOp()
        {
            int clientId = NewClientId();

            _eduRepo.AddRange(Array.Empty<Education>());
            _eduRepo.AddRange(null!);

            Assert.Empty(_eduRepo.GetByClientId(clientId));
        }

        [Fact]
        public void DeleteByClientId_LeavesOtherClientsAlone()
        {
            int first = NewClientId("111111111V");
            int second = NewClientId("222222222V");
            _eduRepo.AddRange(TestData.NewEducation(1).Select(e => { e.ClientId = first; return e; }));
            _eduRepo.AddRange(TestData.NewEducation(1).Select(e => { e.ClientId = second; return e; }));

            _eduRepo.DeleteByClientId(first);

            Assert.Empty(_eduRepo.GetByClientId(first));
            Assert.Single(_eduRepo.GetByClientId(second));
        }

        [Fact]
        public void DeleteClient_CascadesToEducationAndExperience()
        {
            var experienceRepo = new ExperienceRepository();
            int clientId = NewClientId();
            _eduRepo.AddRange(TestData.NewEducation(2).Select(e => { e.ClientId = clientId; return e; }));
            experienceRepo.AddRange(TestData.NewExperience(2).Select(e => { e.ClientId = clientId; return e; }));

            _clientRepo.DeleteClient(clientId);

            Assert.Empty(_eduRepo.GetByClientId(clientId));
            Assert.Empty(experienceRepo.GetByClientId(clientId));
        }

        [Fact]
        public void Add_NullEducation_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _eduRepo.Add(null!));
        }
    }

    public class ExperienceRepositoryTests : IDisposable
    {
        private readonly TempDatabase _db = new();
        private readonly ClientRepository _clientRepo = new();
        private readonly ExperienceRepository _repo = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public void Add_RoundTripsEveryColumn()
        {
            int clientId = _clientRepo.AddClient(TestData.NewClient());

            int id = _repo.Add(new Experience
            {
                ClientId = clientId,
                JobTitle = "Software Engineer",
                Company = "Acme Ltd",
                Duration = "2018 - 2021"
            });

            Assert.True(id > 0);

            Experience saved = _repo.GetByClientId(clientId).Single();
            Assert.Equal("Software Engineer", saved.JobTitle);
            Assert.Equal("Acme Ltd", saved.Company);
            Assert.Equal("2018 - 2021", saved.Duration);
        }

        [Fact]
        public void AddRange_InsertsEveryRow()
        {
            int clientId = _clientRepo.AddClient(TestData.NewClient());
            var rows = TestData.NewExperience(4);
            rows.ForEach(r => r.ClientId = clientId);

            _repo.AddRange(rows);

            Assert.Equal(4, _repo.GetByClientId(clientId).Count);
        }

        [Fact]
        public void DeleteByClientId_RemovesOnlyThatClientsRows()
        {
            int first = _clientRepo.AddClient(TestData.NewClient("A", nic: "111111111V"));
            int second = _clientRepo.AddClient(TestData.NewClient("B", nic: "222222222V"));
            _repo.AddRange(TestData.NewExperience(1).Select(e => { e.ClientId = first; return e; }));
            _repo.AddRange(TestData.NewExperience(2).Select(e => { e.ClientId = second; return e; }));

            _repo.DeleteByClientId(first);

            Assert.Empty(_repo.GetByClientId(first));
            Assert.Equal(2, _repo.GetByClientId(second).Count);
        }

        [Fact]
        public void Add_NullExperience_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _repo.Add(null!));
        }
    }
}
