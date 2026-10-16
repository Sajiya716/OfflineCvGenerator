using System;
using System.Collections.Generic;
using System.Linq;
using OfflineCvBioDataGenerator.Models;
using OfflineCvBioDataGenerator.Repositories;
using OfflineCvBioDataGenerator.Tests.Infrastructure;
using Xunit;

namespace OfflineCvBioDataGenerator.Tests
{
    public class ClientRepositoryTests : IDisposable
    {
        private readonly TempDatabase _db = new();
        private readonly ClientRepository _repo = new();

        public void Dispose() => _db.Dispose();

        // ------------------------------------------------------------------ insert

        [Fact]
        public void AddClient_ReturnsGeneratedId_AndPersistsEveryField()
        {
            var client = TestData.NewClient(nic: "900000000V", dob: new DateTime(1990, 5, 4), email: "jane@example.com");

            int id = _repo.AddClient(client);

            Assert.True(id > 0);

            Client? saved = _repo.GetClientById(id);
            Assert.NotNull(saved);
            Assert.Equal("Jane Doe", saved!.FullName);
            Assert.Equal("900000000V", saved.NIC);
            Assert.Equal("12 Main Street, Colombo", saved.Address);
            Assert.Equal("+94 77 123 4567", saved.Phone);
            Assert.Equal("jane@example.com", saved.Email);
            Assert.Equal(new DateTime(1990, 5, 4), saved.DOB);
            Assert.Equal(string.Empty, saved.PhotoPath);
        }

        [Fact]
        public void AddClient_AssignsDistinctIds()
        {
            int first = _repo.AddClient(TestData.NewClient("First"));
            int second = _repo.AddClient(TestData.NewClient("Second"));

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void AddClient_NullDateOfBirth_StaysNull()
        {
            var client = TestData.NewClient(dob: null);

            int id = _repo.AddClient(client);

            Assert.Null(_repo.GetClientById(id)!.DOB);
        }

        [Fact]
        public void AddClient_StoresPhotoPath()
        {
            var client = TestData.NewClient();
            client.PhotoPath = "ProfilePics\\jane.png";

            int id = _repo.AddClient(client);

            Assert.Equal("ProfilePics\\jane.png", _repo.GetClientById(id)!.PhotoPath);
        }

        [Fact]
        public void AddClient_DuplicateNic_IsRejected()
        {
            _repo.AddClient(TestData.NewClient("First", nic: "111111111V"));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _repo.AddClient(TestData.NewClient("Second", nic: "111111111V")));

            Assert.Contains("111111111V", ex.Message);
        }

        [Fact]
        public void AddClient_NicIsComparedIgnoringSurroundingSpaces()
        {
            _repo.AddClient(TestData.NewClient("First", nic: "222222222V"));

            Assert.Throws<InvalidOperationException>(() =>
                _repo.AddClient(TestData.NewClient("Second", nic: "  222222222V  ")));
        }

        [Fact]
        public void AddClient_NullClient_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _repo.AddClient(null!));
        }

        // ------------------------------------------------------------------ read

        [Fact]
        public void GetClientById_UnknownId_ReturnsNull()
        {
            Assert.Null(_repo.GetClientById(987654));
        }

        [Fact]
        public void GetAllClients_OrdersByNameIgnoringCase()
        {
            _repo.AddClient(TestData.NewClient("charlie"));
            _repo.AddClient(TestData.NewClient("Alice"));
            _repo.AddClient(TestData.NewClient("bob"));

            List<string> names = _repo.GetAllClients().Select(c => c.FullName).ToList();

            Assert.Equal(new[] { "Alice", "bob", "charlie" }, names);
        }

        // ------------------------------------------------------------------ update

        [Fact]
        public void Save_WithExistingId_UpdatesInsteadOfInserting()
        {
            int id = _repo.AddClient(TestData.NewClient());

            var edited = _repo.GetClientById(id)!;
            edited.FullName = "Jane A. Doe";
            edited.Phone = "0770000000";
            _repo.Save(edited, null, null);

            Assert.Single(_repo.GetAllClients());
            Assert.Equal("Jane A. Doe", _repo.GetClientById(id)!.FullName);
            Assert.Equal("0770000000", _repo.GetClientById(id)!.Phone);
        }

        [Fact]
        public void UpdateClient_UnknownId_Throws()
        {
            var ghost = TestData.NewClient();
            ghost.Id = 424242;

            Assert.Throws<InvalidOperationException>(() => _repo.UpdateClient(ghost));
        }

        // ------------------------------------------------------------------ delete

        [Fact]
        public void DeleteClient_RemovesTheClient()
        {
            int id = _repo.AddClient(TestData.NewClient());

            _repo.DeleteClient(id);

            Assert.Null(_repo.GetClientById(id));
        }

        [Fact]
        public void DeleteClient_UnknownId_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _repo.DeleteClient(424242));
        }

        // ------------------------------------------------------------------ save (transactional)

        [Fact]
        public void Save_NewClient_WritesClientEducationAndExperienceInOneGo()
        {
            var client = TestData.NewClient();

            Client saved = _repo.Save(client, TestData.NewEducation(2), TestData.NewExperience(3));

            Assert.True(saved.Id > 0);
            Assert.Equal(2, new EducationRepository().GetByClientId(saved.Id).Count);
            Assert.Equal(3, new ExperienceRepository().GetByClientId(saved.Id).Count);
        }

        [Fact]
        public void Save_ExistingClient_ReplacesChildRowsInsteadOfAppending()
        {
            var client = TestData.NewClient();
            _repo.Save(client, TestData.NewEducation(3), TestData.NewExperience(3));

            _repo.Save(client, TestData.NewEducation(1), TestData.NewExperience(1));

            Assert.Single(new EducationRepository().GetByClientId(client.Id));
            Assert.Single(new ExperienceRepository().GetByClientId(client.Id));
        }

        [Fact]
        public void Save_WithEmptyChildLists_ClearsThem()
        {
            var client = TestData.NewClient();
            _repo.Save(client, TestData.NewEducation(2), TestData.NewExperience(2));

            _repo.Save(client, new List<Education>(), new List<Experience>());

            Assert.Empty(new EducationRepository().GetByClientId(client.Id));
            Assert.Empty(new ExperienceRepository().GetByClientId(client.Id));
        }

        [Fact]
        public void Save_NullChildLists_LeavesExistingRowsAlone()
        {
            var client = TestData.NewClient();
            _repo.Save(client, TestData.NewEducation(2), TestData.NewExperience(2));

            _repo.Save(client, null, null);

            Assert.Equal(2, new EducationRepository().GetByClientId(client.Id).Count);
            Assert.Equal(2, new ExperienceRepository().GetByClientId(client.Id).Count);
        }

        [Fact]
        public void Save_FailingValidation_RollsBackEverything()
        {
            _repo.AddClient(TestData.NewClient("Existing", nic: "333333333V"));

            var duplicate = TestData.NewClient("Duplicate", nic: "333333333V");

            Assert.Throws<InvalidOperationException>(() =>
                _repo.Save(duplicate, TestData.NewEducation(2), TestData.NewExperience(2)));

            // Nothing at all should have been written by the failed save.
            Assert.Single(_repo.GetAllClients());
            Assert.DoesNotContain(_repo.GetAllClients(), c => c.FullName == "Duplicate");

            // ...and a subsequent (valid) save still works.
            int okId = _repo.AddClient(TestData.NewClient("Ok", nic: "444444444V"));
            Assert.True(okId > 0);
            Assert.Equal(2, _repo.GetAllClients().Count);
        }

        [Fact]
        public void Save_SetsChildClientIds()
        {
            var client = TestData.NewClient();
            var education = TestData.NewEducation(1);
            var experience = TestData.NewExperience(1);

            _repo.Save(client, education, experience);

            Assert.Equal(client.Id, education[0].ClientId);
            Assert.Equal(client.Id, experience[0].ClientId);
        }

        // ------------------------------------------------------------------ search

        [Fact]
        public void SearchClients_MatchesPartOfNameIgnoringCase()
        {
            _repo.AddClient(TestData.NewClient("Sunil Perera", nic: "555555555V"));
            _repo.AddClient(TestData.NewClient("Kamal Silva", nic: "666666666V"));

            var results = _repo.SearchClients("perer");

            Assert.Single(results);
            Assert.Equal("Sunil Perera", results[0].FullName);
        }

        [Fact]
        public void SearchClients_MatchesNic()
        {
            _repo.AddClient(TestData.NewClient("Sunil Perera", nic: "555555555V"));
            _repo.AddClient(TestData.NewClient("Kamal Silva", nic: "666666666V"));

            var results = _repo.SearchClients("666");

            Assert.Single(results);
            Assert.Equal("Kamal Silva", results[0].FullName);
        }

        [Fact]
        public void SearchClients_EscapesUnderscoreWildcard()
        {
            _repo.AddClient(TestData.NewClient("Al_ha", nic: "777777777V"));
            _repo.AddClient(TestData.NewClient("Alpha", nic: "888888888V"));

            var results = _repo.SearchClients("Al_");

            // Without escaping, '_' would be a single-character wildcard and match
            // "Alpha" as well.
            Assert.Single(results);
            Assert.Equal("Al_ha", results[0].FullName);
        }

        [Fact]
        public void SearchClients_EscapesPercentWildcard()
        {
            _repo.AddClient(TestData.NewClient("100% Cotton", nic: "101010101V"));
            _repo.AddClient(TestData.NewClient("1000 Cotton", nic: "202020202V"));

            var results = _repo.SearchClients("100%");

            Assert.Single(results);
            Assert.Equal("100% Cotton", results[0].FullName);
        }

        [Fact]
        public void SearchClients_NoMatch_ReturnsEmptyList()
        {
            _repo.AddClient(TestData.NewClient("Sunil Perera"));

            Assert.Empty(_repo.SearchClients("zzzz-nothing"));
        }

        [Fact]
        public void NicExists_IgnoresTheClientBeingEdited()
        {
            int id = _repo.AddClient(TestData.NewClient(nic: "999999999V"));

            Assert.True(_repo.NicExists("999999999V"));
            Assert.False(_repo.NicExists("999999999V", excludeClientId: id));
        }

        [Fact]
        public void NicExists_BlankNic_IsNeverADuplicate()
        {
            Assert.False(_repo.NicExists("   "));
            Assert.False(_repo.NicExists(string.Empty));
        }
    }
}
