﻿using System;
using System.Collections.Generic;
using Calamari.Common.Plumbing.Deployment.PackageRetention;
using Calamari.Common.Plumbing.Logging;
using Calamari.Common.Plumbing.Variables;
using Calamari.Deployment.PackageRetention.Repositories;
using Octopus.Versioning;

namespace Calamari.Deployment.PackageRetention.Model
{
    public class Journal : IJournal
    {
        readonly IJournalRepositoryFactory repositoryFactory;
        readonly ILog log;

        public Journal(IJournalRepositoryFactory repositoryFactory, ILog log)
        {
            this.repositoryFactory = repositoryFactory;
            this.log = log;
        }

        public void RegisterPackageUse(string packageID, string version, string serverTaskID)
        {
            RegisterPackageUse(new PackageIdentity(packageID, version), new ServerTaskID(serverTaskID));
        }

        public void RegisterPackageUse(IVariables variables)
        {
            RegisterPackageUse(new PackageIdentity(variables), new ServerTaskID(variables));
        }

        public void RegisterPackageUse(PackageIdentity package, ServerTaskID serverTaskID)
        {
            try
            {
                var repository = repositoryFactory.CreateJournalRepository();

                if (repository.TryGetJournalEntry(package, out var entry))
                {
                    entry.PackageUsage.AddUsage(serverTaskID);
                    entry.PackageLocks.AddLock(serverTaskID);
                }
                else
                {
                    entry = new JournalEntry(package);
                    entry.PackageUsage.AddUsage(serverTaskID);
                    entry.PackageLocks.AddLock(serverTaskID);
                    repository.AddJournalEntry(entry);
                }

#if DEBUG
                log.Verbose($"Registered package use/lock for {package} and task {serverTaskID}");
#endif

                repository.Commit();
            }
            catch (Exception ex)
            {
                //We need to ensure that an issue with the journal doesn't interfere with the deployment.
                log.Error($"Unable to register package use for retention.{Environment.NewLine}{ex.ToString()}");
            }
        }

        public void DeregisterPackageUse(PackageIdentity package, ServerTaskID serverTaskID)
        {
            try
            {
                var repository = repositoryFactory.CreateJournalRepository();

                if (repository.TryGetJournalEntry(package, out var entry))
                {
                    entry.PackageLocks.RemoveLock(serverTaskID);
                }
            }
            catch (Exception ex)
            {
                //We need to ensure that an issue with the journal doesn't interfere with the deployment.
                log.Error($"Unable to deregister package use for retention.{Environment.NewLine}{ex.ToString()}");
            }
        }

        public bool HasLock(PackageIdentity package)
        {
            return repositoryFactory.CreateJournalRepository()
                                    .TryGetJournalEntry(package, out var entry)
                   && entry.PackageLocks.HasLock();
        }

        public IEnumerable<DateTime> GetUsage(PackageIdentity package)
        {
            return repositoryFactory.CreateJournalRepository()
                                    .TryGetJournalEntry(package, out var entry)
                ? entry.PackageUsage.GetUsageDetails()
                : new DateTime[0];
        }

        public void ExpireStaleLocks()
        {
            throw new NotImplementedException();
        }
    }
}