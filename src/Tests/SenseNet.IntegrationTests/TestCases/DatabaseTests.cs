
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.ContentRepository.Storage;
using SenseNet.IntegrationTests.Infrastructure;

namespace SenseNet.IntegrationTests.TestCases
{
    public class DatabaseTests : TestCaseBase
    {
        public void DB_ReferenceProperties(Func<Node, PropertyType, int[]> GetReferencesFromDatabase)
        {
            Cache.Reset();

            IsolatedIntegrationTest(() =>
            {
                var group = Group.Administrators;
                var expectedIds = group.Members.Select(x => x.Id).ToList();
                var propertyType = ActiveSchema.PropertyTypes["Members"];
                var before = GetReferencesFromDatabase(group, propertyType);

                var user = new User(OrganizationalUnit.Portal)
                {
                    Name = "User-1",
                    Email = "user1@example.com",
                    Enabled = true
                };
                user.Save();
                expectedIds.Add(user.Id);

                Group.Administrators.AddMember(user);

                var after = GetReferencesFromDatabase(group, propertyType);

                // ASSERT
                Assert.AreEqual(string.Join(",", expectedIds.OrderBy(x => x)),
                    string.Join(",", after.OrderBy(x => x)));
            });
        }
    }
}
