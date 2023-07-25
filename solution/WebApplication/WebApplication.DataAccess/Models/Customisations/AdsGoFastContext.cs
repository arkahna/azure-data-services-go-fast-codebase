﻿using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace WebApplication.Models
{
    public partial class AdsGoFastContext : DbContext
    {
        partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
        {

            modelBuilder.Entity<ScheduleInstance>(entity =>
            {
                entity.HasOne<ScheduleMaster>(si => si.ScheduleMaster).WithMany(sm => sm.ScheduleInstances).HasForeignKey(si => si.ScheduleMasterId);

            });
            


            modelBuilder.Entity<ScheduleMaster>(entity =>
            {
                entity.HasAnnotation("DisplayColumn", "ScheduleDesciption");
                entity.Property(e => e.ActiveYn).HasColumnName("ActiveYn");

                entity.Property(e => e.ScheduleDesciption)
                    .IsRequired()
                    .HasMaxLength(200)
                    .IsUnicode(false);

                entity.Property(e => e.ScheduleCronExpression)
                    .IsRequired()
                    .HasMaxLength(200);
                entity.HasOne<MetadataExtractionVersion>(tm => tm.MetadataExtractionVersion).WithMany(tt => tt.ScheduleMasters).HasForeignKey(tm => tm.ExtractionVersionId);


            });

            modelBuilder.Entity<SourceAndTargetSystems>(entity =>
            {
                entity.HasAnnotation("DisplayColumn", "SystemName");
                entity.HasOne<MetadataExtractionVersion>(tm => tm.MetadataExtractionVersion).WithMany(tt => tt.SourceAndTargetSystems).HasForeignKey(tm => tm.ExtractionVersionId);

            });

            modelBuilder.Entity<IntegrationRuntime>(entity =>
            {
                entity.HasOne<ExecutionEngine>(tm => tm.ExecutionEngine).WithMany(tt => tt.IntegrationRuntime).HasForeignKey(tm => tm.EngineId);
            });



            modelBuilder.Entity<ExecutionEngine>(entity =>
            {
                entity.HasAnnotation("DisplayColumn", "EngineName");
            });

            modelBuilder.Entity<TaskMaster>(entity =>
            {

                entity.HasAnnotation("DisplayColumn", "TaskMasterName");
                entity.HasMany<TaskInstance>(tm => tm.TaskInstances).WithOne(ti => ti.TaskMaster).HasForeignKey(ti => ti.TaskMasterId);
                entity.HasOne<TaskGroup>(tm => tm.TaskGroup).WithMany(tg => tg.TaskMasters).HasForeignKey(tm => tm.TaskGroupId);
                entity.HasOne<TaskType>(tm=> tm.TaskType).WithMany(tt => tt.TaskMasters).HasForeignKey(tm => tm.TaskTypeId);
                entity.HasOne<ScheduleMaster>(tm => tm.ScheduleMaster).WithMany(sm => sm.TaskMasters).HasForeignKey(tm => tm.ScheduleMasterId) ;
                entity.HasOne<SourceAndTargetSystems>(tm => tm.SourceSystem).WithMany(tt => tt.TaskMastersSource).HasForeignKey(tm => tm.SourceSystemId);
                entity.HasOne<SourceAndTargetSystems>(tm => tm.TargetSystem).WithMany(tt => tt.TaskMastersTarget).HasForeignKey(tm => tm.TargetSystemId);
                entity.HasOne<ExecutionEngine>(tm => tm.ExecutionEngine).WithMany(tt => tt.TaskMasters).HasForeignKey(tm => tm.EngineId);
                entity.HasOne<MetadataExtractionVersion>(tm => tm.MetadataExtractionVersion).WithMany(tt => tt.TaskMasters).HasForeignKey(tm => tm.ExtractionVersionId);

                entity.Property(p => p.TaskDatafactoryIr).IsRequired();
            });


            modelBuilder.Entity<TaskInstance>(entity =>
            {
                entity.HasAnnotation("DisplayColumn", "Description");
                entity.HasOne<TaskMaster>(tm => tm.TaskMaster).WithMany(t => t.TaskInstances).HasForeignKey(t => t.TaskMasterId);
                entity.HasOne<ScheduleInstance>(tm => tm.ScheduleInstance).WithMany(t => t.TaskInstances).HasForeignKey(t => t.ScheduleInstanceId);
                entity.Property(e => e.LastExecutionStatus).HasConversion(
                    v => v.ToString(),
                    v => (TaskExecutionStatus)Enum.Parse(typeof(TaskExecutionStatus), v));
                    

            });


            modelBuilder.Entity<TaskGroup>(entity =>
            {
                entity.HasAnnotation("DisplayColumn", "TaskGroupName");
                entity.Property(p => p.TaskGroupName).IsRequired();
                entity.HasMany<TaskMaster>(tm => tm.TaskMasters).WithOne(tg => tg.TaskGroup).HasForeignKey(tm => tm.TaskGroupId);
                entity.HasOne<SubjectArea>(tg => tg.SubjectArea).WithMany(sa => sa.TaskGroups).HasForeignKey(tg => tg.SubjectAreaId);
                entity.HasOne<MetadataExtractionVersion>(tm => tm.MetadataExtractionVersion).WithMany(tt => tt.TaskGroups).HasForeignKey(tm => tm.ExtractionVersionId);

            });

            modelBuilder.Entity<TaskGroupDependency>(entity =>
            {
                entity.HasOne<MetadataExtractionVersion>(tm => tm.MetadataExtractionVersion).WithMany(tt => tt.TaskGroupDependencies).HasForeignKey(tm => tm.ExtractionVersionId);

            });

            modelBuilder.Entity<TaskType>().Property(e => e.TaskExecutionType).HasConversion(
                v => v.ToString(),
                v => (TaskExecutionTypeEnum)Enum.Parse(typeof(TaskExecutionTypeEnum), v));

            modelBuilder.Entity<SubjectArea>(entity =>
            {
                entity.HasOne<SubjectAreaForm>(tg => tg.SubjectAreaForm).WithMany(sa => sa.SubjectAreas).HasForeignKey(tg => tg.SubjectAreaFormId);
                entity.HasOne<MetadataExtractionVersion>(tm => tm.MetadataExtractionVersion).WithMany(tt => tt.SubjectAreas).HasForeignKey(tm => tm.ExtractionVersionId);

            });


            modelBuilder.Entity<TaskGroupStats>(entity =>
            {
                entity.HasNoKey();
            });

            modelBuilder.Entity<TaskInstanceExecution>(entity =>
            {
                entity.HasAnnotation("DisplayColumn", "Description");
                entity.HasOne<TaskInstance>(ti => ti.TaskInstance).WithMany(ie => ie.TaskInstanceExecutions).HasForeignKey(ti => ti.TaskInstanceId);
                entity.HasOne<Execution>(ti => ti.Execution).WithMany(ie => ie.TaskInstanceExecutions).HasForeignKey(ti => ti.ExecutionUid);

            });

            modelBuilder.Entity<TaskTypeMapping>(entity =>
            {

                entity.HasOne<TaskType>(ti => ti.TaskType).WithMany(ie => ie.TaskTypeMappings).HasForeignKey(ti => ti.TaskTypeId);

            });

            modelBuilder.Entity<IntegrationRuntimeMapping>(entity =>
            {

                entity.HasOne<SourceAndTargetSystems>(ti => ti.SourceAndTargetSystem).WithMany(ie => ie.IntegrationRuntimeMappings).HasForeignKey(ti => ti.SystemId);

            });

            modelBuilder.Entity<AdfpipelineStats1>(entity =>
            {

                //entity.HasOne<TaskInstanceExecution>(ti => ti.TaskInstanceExecution).WithMany(ie => ie.AdfPipelineStats)
                //    .HasForeignKey(ti => new {ti.TaskInstanceId, ti.ExecutionUid});

            });

            modelBuilder.Entity<TaskMasterWaterMark>(entity =>
            {
                entity.HasAnnotation("DisplayColumn", "Description");
                entity.HasOne<TaskMaster>(ti => ti.TaskMaster).WithOne();
            });

        }

        public IQueryable<EntityRoleMap> GetEntityRoleMapsFor(string entityTypeName, Guid[] assignedAdGroups, string[] applicationRoles)
        {
            return
                from r in this.EntityRoleMap
                where assignedAdGroups.Contains(r.AadGroupUid)
                    && applicationRoles.Contains(r.ApplicationRoleName)
                    && r.EntityTypeName == entityTypeName
                    && r.ExpiryDate > DateTimeOffset.Now
                      && r.ActiveYN
                select r;
        }
    }
}
