using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Majordomo;
using Majordomo_Protocol;
using Quartz;
using Quartz.Impl;

namespace Clerk
{
    /// <summary>
    /// An abstract class that implements Quartz.Net's IJob interface and also provides a Majordomo Client interface for sending out
    ///  requests to Job endpoints.
    /// This is just here to make writing Job implementations easier. 
    /// </summary>
    public abstract class RequestMaker : IJob
    {
        private string _address = "tcp://127.0.0.1";
        private string _service = "janitor";

        private MajordomoClient _client;

        public MajordomoClient Client
        {
            get
            {
                if (_client == null || !_client.Service.Equals(_service))
                    _client = new MajordomoClient(_address, _service);

                return _client;
            }
        }

        public abstract void Execute(IJobExecutionContext context);
    }

    /// <summary>
    /// A job implementation for handling critical errors using the janitor. Critical errors are handled by emailing the admin.
    /// </summary>
    public class CriticalErrorHandleJob : RequestMaker
    {
        public override void Execute(IJobExecutionContext context)
        {
            var message = new List<byte[]>()
            {
                "EmailCriticalErrorsPastWeek".ToBytes()
            };

            Client.SendReceiveString(message);
        }
    }

    /// <summary>
    /// A job implementation to generate critical errors using the janitor.
    /// These critical errors are generated from unrecoverable error states.
    /// </summary>
    public class CriticalErrorGenerateJob : RequestMaker
    {
        public override void Execute(IJobExecutionContext context)
        {
            var message = new List<byte[]>()
            {
                "GenerateCriticalErrors".ToBytes()
            };

            Client.SendReceiveString(message);
        }
    }

    /// <summary>
    /// A job implementation for replaying orphaned requests in the janitor
    /// </summary>
    public class ReplayOrphanJob : RequestMaker
    {
        public override void Execute(IJobExecutionContext context)
        {
            var message = new List<byte[]>()
            {
                "ReplayOrphans".ToBytes()
            };

            Client.SendReceiveString(message);
        }
    }

    /// <summary>
    /// This is an overseer that runs timed tasks. It uses Quartz.Net (a .NET port of the Quartz library for Java) for timekeeping
    /// </summary>
    public class Clerk
    {
        public void Work()
        {
            var schedulerFactory = new StdSchedulerFactory();
            var scheduler = schedulerFactory.GetScheduler();
            scheduler.Start();  

            var orphanJob = JobBuilder.Create<ReplayOrphanJob>()
                .WithIdentity("replayOrphanJob", "janitorGroup")
                .Build();

            // Cronjob trigger to run at midnight every day
            var orphanTrigger = TriggerBuilder.Create()
              .WithIdentity("replayTrigger", "janitorGroup")
              .WithCronSchedule("00 00 * * *")
              .StartNow()
              .Build();

            scheduler.ScheduleJob(orphanJob, orphanTrigger);

            var errorGenerateJob = JobBuilder.Create<CriticalErrorGenerateJob>()
                .WithIdentity("criticalErrorGenerateJob", "janitorGroup")
                .Build();

            // Cronjob trigger to run at midnight on saturday every week
            var errorGenerateTrigger = TriggerBuilder.Create()
              .WithIdentity("criticalErrorGenerateTrigger", "janitorGroup")
              .WithCronSchedule("00 00 * * 6")
              .StartNow()
              .Build();

            scheduler.ScheduleJob(errorGenerateJob, errorGenerateTrigger);

            var errorProcessJob = JobBuilder.Create<CriticalErrorHandleJob>()
                .WithIdentity("errorProcessJob", "janitorGroup")
                .Build();

            // Cronjob trigger to run at midnight on saturday every week
            var errorProcessTrigger = TriggerBuilder.Create()
              .WithIdentity("errorProcessTrigger", "janitorGroup")
              .WithCronSchedule("00 00 * * 6")
              .StartNow()
              .Build();

            scheduler.ScheduleJob(errorProcessJob, errorProcessTrigger);

            while (true)
                Thread.Sleep(100);
        }
    }

    public class DeferredTask
    {
        
    }
}
