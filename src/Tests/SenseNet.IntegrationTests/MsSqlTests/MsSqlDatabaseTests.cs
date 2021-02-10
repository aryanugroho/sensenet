using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Data.MsSqlClient;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.IntegrationTests.Infrastructure;
using SenseNet.IntegrationTests.Platforms;
using SenseNet.IntegrationTests.TestCases;

namespace SenseNet.IntegrationTests.MsSqlTests
{
    [TestClass]
    public class MsSqlDatabaseTests : IntegrationTest<MsSqlPlatform, DatabaseTests>
    {
        public async Task<int[]> GetReferencesFromDbAsync(Node node, PropertyType propertyType, CancellationToken cancellation)
        {
            var sql = "SELECT ReferredNodeId FROM ReferenceProperties " +
                      "WHERE VersionId = @VersionId AND PropertyTypeId = @PropertyTypeId";

            using (var ctx = new MsSqlDataContext(cancellation))
            {
                return await ctx.ExecuteReaderAsync(sql, cmd =>
                {
                    cmd.Parameters.AddRange(new[]
                    {
                        ctx.CreateParameter("@VersionId", DbType.Int32, node.VersionId),
                        ctx.CreateParameter("@PropertyTypeId", DbType.Int32, propertyType.Id)

                    });
                }, async (reader, cancel) =>
                {
                    cancel.ThrowIfCancellationRequested();
                    var result = new List<int>();
                    while (await reader.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        cancel.ThrowIfCancellationRequested();
                        result.Add(reader.GetSafeInt32(0));
                    }
                    return result.ToArray();
                }).ConfigureAwait(false);
            }
        }

        private int[] GetReferencesFromDb(Node node, PropertyType propertyType)
        {
            return GetReferencesFromDbAsync(node, propertyType, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void IntT_MsSql_DB_ReferenceProperties()
        {
            TestCase.DB_ReferenceProperties(GetReferencesFromDb);
        }
    }
}
