using Microsoft.EntityFrameworkCore;

namespace TNLAStation.Infrastructure.Persistence;

public sealed class EpgDbContext(DbContextOptions<EpgDbContext> options) : DbContext(options)
{
    public DbSet<EpgChannelEntity> Channels => Set<EpgChannelEntity>();

    public DbSet<EpgProgramEntity> Programs => Set<EpgProgramEntity>();

    public DbSet<EpgSyncStateEntity> SyncStates => Set<EpgSyncStateEntity>();

    public DbSet<RuleEntity> Rules => Set<RuleEntity>();

    public DbSet<ManualReserveEntity> ManualReserves => Set<ManualReserveEntity>();

    public DbSet<ReserveEntity> Reserves => Set<ReserveEntity>();

    public DbSet<ReserveSkipEntity> ReserveSkips => Set<ReserveSkipEntity>();

    public DbSet<RecordedEntity> Recorded => Set<RecordedEntity>();

    public DbSet<VideoFileEntity> VideoFiles => Set<VideoFileEntity>();

    public DbSet<RecordedTagEntity> RecordedTags => Set<RecordedTagEntity>();

    public DbSet<RecordedTagLinkEntity> RecordedTagLinks => Set<RecordedTagLinkEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureModel(modelBuilder);
    }

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {

        modelBuilder.Entity<EpgChannelEntity>(entity =>
        {
            entity.ToTable("channels", table =>
            {
                table.HasCheckConstraint("ck_channels_type", "channel_type IN ('GR', 'BS', 'CS', 'SKY')");
                table.HasCheckConstraint(
                    "ck_channels_type_id",
                    "channel_type_id = CASE channel_type WHEN 'GR' THEN 0 WHEN 'BS' THEN 1 " +
                    "WHEN 'CS' THEN 2 WHEN 'SKY' THEN 3 END");
            });
            entity.HasKey(item => item.Id).HasName("pk_channels");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(item => item.ServiceId).HasColumnName("service_id");
            entity.Property(item => item.NetworkId).HasColumnName("network_id");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.HalfWidthName).HasColumnName("half_width_name");
            entity.Property(item => item.RemoteControlKeyId).HasColumnName("remote_control_key_id");
            entity.Property(item => item.HasLogoData).HasColumnName("has_logo_data").HasDefaultValue(false);
            entity.Property(item => item.ChannelTypeId).HasColumnName("channel_type_id");
            entity.Property(item => item.ChannelType).HasColumnName("channel_type");
            entity.Property(item => item.Channel).HasColumnName("channel");
            entity.Property(item => item.ServiceType).HasColumnName("service_type");
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.HasIndex(item => new { item.NetworkId, item.ServiceId })
                .IsUnique()
                .HasDatabaseName("uq_channels_network_service");
            entity.HasAlternateKey(item => new { item.Id, item.NetworkId, item.ServiceId })
                .HasName("ak_channels_identity");
            entity.HasIndex(item => new { item.ChannelTypeId, item.RemoteControlKeyId, item.ServiceId })
                .HasDatabaseName("ix_channels_display_order");
        });

        modelBuilder.Entity<EpgProgramEntity>(entity =>
        {
            entity.ToTable("programs", table =>
            {
                table.HasCheckConstraint("ck_programs_time", "end_at >= start_at AND duration_ms >= 0");
                table.HasCheckConstraint("ck_programs_start_hour", "start_hour BETWEEN 0 AND 23");
                table.HasCheckConstraint("ck_programs_week", "week BETWEEN 0 AND 6");
            });
            entity.HasKey(item => item.Id).HasName("pk_programs");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(item => item.UpdateTime).HasColumnName("update_time").HasColumnType("timestamp with time zone");
            entity.Property(item => item.ChannelId).HasColumnName("channel_id");
            entity.Property(item => item.EventId).HasColumnName("event_id");
            entity.Property(item => item.ServiceId).HasColumnName("service_id");
            entity.Property(item => item.NetworkId).HasColumnName("network_id");
            entity.Property(item => item.StartAt).HasColumnName("start_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.EndAt).HasColumnName("end_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.StartHour).HasColumnName("start_hour");
            entity.Property(item => item.Week).HasColumnName("week");
            entity.Property(item => item.DurationMilliseconds).HasColumnName("duration_ms");
            entity.Property(item => item.IsFree).HasColumnName("is_free");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.HalfWidthName).HasColumnName("half_width_name");
            entity.Property(item => item.ShortName).HasColumnName("short_name");
            entity.Property(item => item.Description).HasColumnName("description");
            entity.Property(item => item.HalfWidthDescription).HasColumnName("half_width_description");
            entity.Property(item => item.Extended).HasColumnName("extended");
            entity.Property(item => item.HalfWidthExtended).HasColumnName("half_width_extended");
            entity.Property(item => item.RawExtendedJson).HasColumnName("raw_extended").HasColumnType("json");
            entity.Property(item => item.RawHalfWidthExtendedJson).HasColumnName("raw_half_width_extended").HasColumnType("json");
            entity.Property(item => item.Genre1).HasColumnName("genre1");
            entity.Property(item => item.SubGenre1).HasColumnName("sub_genre1");
            entity.Property(item => item.Genre2).HasColumnName("genre2");
            entity.Property(item => item.SubGenre2).HasColumnName("sub_genre2");
            entity.Property(item => item.Genre3).HasColumnName("genre3");
            entity.Property(item => item.SubGenre3).HasColumnName("sub_genre3");
            entity.Property(item => item.ChannelType).HasColumnName("channel_type");
            entity.Property(item => item.Channel).HasColumnName("channel");
            entity.Property(item => item.VideoType).HasColumnName("video_type");
            entity.Property(item => item.VideoResolution).HasColumnName("video_resolution");
            entity.Property(item => item.VideoStreamContent).HasColumnName("video_stream_content");
            entity.Property(item => item.VideoComponentType).HasColumnName("video_component_type");
            entity.Property(item => item.AudioSamplingRate).HasColumnName("audio_sampling_rate");
            entity.Property(item => item.AudioComponentType).HasColumnName("audio_component_type");
            entity.HasIndex(item => new { item.NetworkId, item.ServiceId, item.EventId })
                .IsUnique()
                .HasDatabaseName("uq_programs_event");
            entity.HasIndex(item => item.EndAt).HasDatabaseName("ix_programs_end_at");
            entity.HasIndex(item => item.StartAt).HasDatabaseName("ix_programs_start_at");
            entity.HasIndex(item => new { item.ChannelId, item.StartAt, item.EndAt })
                .HasDatabaseName("ix_programs_channel_start");
            entity.HasIndex(item => new { item.ChannelType, item.StartAt, item.EndAt })
                .HasDatabaseName("ix_programs_type_start");
            entity.HasIndex(item => new { item.Week, item.StartHour, item.StartAt })
                .HasDatabaseName("ix_programs_week_hour");
            entity.HasIndex(item => new { item.Genre1, item.SubGenre1 }).HasDatabaseName("ix_programs_genre1");
            entity.HasIndex(item => new { item.Genre2, item.SubGenre2 }).HasDatabaseName("ix_programs_genre2");
            entity.HasIndex(item => new { item.Genre3, item.SubGenre3 }).HasDatabaseName("ix_programs_genre3");
            entity.HasOne(item => item.ChannelEntity)
                .WithMany(channel => channel.Programs)
                .HasForeignKey(item => new { item.ChannelId, item.NetworkId, item.ServiceId })
                .HasPrincipalKey(channel => new { channel.Id, channel.NetworkId, channel.ServiceId })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_programs_channel");
        });

        modelBuilder.Entity<RuleEntity>(entity =>
        {
            entity.ToTable("rules");
            entity.HasKey(item => item.Id).HasName("pk_rules");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(item => item.UpdateCount).HasColumnName("update_cnt").HasDefaultValue(0L);
            entity.Property(item => item.IsTimeSpecification).HasColumnName("is_time_specification");
            entity.Property(item => item.Priority).HasColumnName("priority").HasDefaultValue(0);
            entity.Property(item => item.Keyword).HasColumnName("keyword");
            entity.Property(item => item.HalfWidthKeyword).HasColumnName("half_width_keyword");
            entity.Property(item => item.IgnoreKeyword).HasColumnName("ignore_keyword");
            entity.Property(item => item.HalfWidthIgnoreKeyword).HasColumnName("half_width_ignore_keyword");
            entity.Property(item => item.KeyCaseSensitive).HasColumnName("key_cs");
            entity.Property(item => item.KeyRegularExpression).HasColumnName("key_reg_exp");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.Description).HasColumnName("description");
            entity.Property(item => item.Extended).HasColumnName("extended");
            entity.Property(item => item.IgnoreKeyCaseSensitive).HasColumnName("ignore_key_cs");
            entity.Property(item => item.IgnoreKeyRegularExpression).HasColumnName("ignore_key_reg_exp");
            entity.Property(item => item.IgnoreName).HasColumnName("ignore_name");
            entity.Property(item => item.IgnoreDescription).HasColumnName("ignore_description");
            entity.Property(item => item.IgnoreExtended).HasColumnName("ignore_extended");
            entity.Property(item => item.Gr).HasColumnName("gr");
            entity.Property(item => item.Bs).HasColumnName("bs");
            entity.Property(item => item.Cs).HasColumnName("cs");
            entity.Property(item => item.Sky).HasColumnName("sky");
            entity.Property(item => item.ChannelIdsJson).HasColumnName("channel_ids").HasColumnType("json");
            entity.Property(item => item.GenresJson).HasColumnName("genres").HasColumnType("json");
            entity.Property(item => item.TimesJson).HasColumnName("times").HasColumnType("json");
            entity.Property(item => item.IsFree).HasColumnName("is_free");
            entity.Property(item => item.DurationMin).HasColumnName("duration_min");
            entity.Property(item => item.DurationMax).HasColumnName("duration_max");
            entity.Property(item => item.SearchPeriodsJson).HasColumnName("search_periods").HasColumnType("json");
            entity.Property(item => item.Enable).HasColumnName("enable");
            entity.Property(item => item.AvoidDuplicate).HasColumnName("avoid_duplicate");
            entity.Property(item => item.PeriodToAvoidDuplicate).HasColumnName("period_to_avoid_duplicate");
            entity.Property(item => item.AllowEndLack).HasColumnName("allow_end_lack").HasDefaultValue(true);
            entity.Property(item => item.TagsJson).HasColumnName("tags").HasColumnType("json");
            entity.Property(item => item.ParentDirectoryName).HasColumnName("parent_directory_name");
            entity.Property(item => item.Directory).HasColumnName("directory");
            entity.Property(item => item.RecordedFormat).HasColumnName("recorded_format");
            entity.Property(item => item.Mode1).HasColumnName("mode1");
            entity.Property(item => item.ParentDirectoryName1).HasColumnName("parent_directory_name1");
            entity.Property(item => item.Directory1).HasColumnName("directory1");
            entity.Property(item => item.Mode2).HasColumnName("mode2");
            entity.Property(item => item.ParentDirectoryName2).HasColumnName("parent_directory_name2");
            entity.Property(item => item.Directory2).HasColumnName("directory2");
            entity.Property(item => item.Mode3).HasColumnName("mode3");
            entity.Property(item => item.ParentDirectoryName3).HasColumnName("parent_directory_name3");
            entity.Property(item => item.Directory3).HasColumnName("directory3");
            entity.Property(item => item.IsDeleteOriginalAfterEncode)
                .HasColumnName("is_delete_original_after_encode");
            entity.HasIndex(item => item.HalfWidthKeyword).HasDatabaseName("ix_rules_half_width_keyword");
            entity.HasIndex(item => item.Enable).HasDatabaseName("ix_rules_enable");
        });

        modelBuilder.Entity<ManualReserveEntity>(entity =>
        {
            entity.ToTable("manual_reserves", table =>
            {
                table.HasCheckConstraint("ck_manual_reserves_time", "end_at > start_at");
                table.HasCheckConstraint(
                    "ck_manual_reserves_target",
                    "program_id IS NOT NULL OR is_time_specified");
            });
            entity.HasKey(item => item.Id).HasName("pk_manual_reserves");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(item => item.ProgramId).HasColumnName("program_id");
            entity.Property(item => item.IsTimeSpecified).HasColumnName("is_time_specified");
            entity.Property(item => item.ChannelId).HasColumnName("channel_id");
            entity.Property(item => item.ChannelType).HasColumnName("channel_type");
            entity.Property(item => item.StartAt).HasColumnName("start_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.EndAt).HasColumnName("end_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.HalfWidthName).HasColumnName("half_width_name");
            entity.Property(item => item.AllowEndLack).HasColumnName("allow_end_lack").HasDefaultValue(true);
            entity.Property(item => item.Priority).HasColumnName("priority").HasDefaultValue(0);
            entity.Property(item => item.IsDeleteOriginalAfterEncode)
                .HasColumnName("is_delete_original_after_encode");
            entity.Property(item => item.TagsJson).HasColumnName("tags").HasColumnType("json");
            entity.Property(item => item.ParentDirectoryName).HasColumnName("parent_directory_name");
            entity.Property(item => item.Directory).HasColumnName("directory");
            entity.Property(item => item.RecordedFormat).HasColumnName("recorded_format");
            entity.Property(item => item.Mode1).HasColumnName("mode1");
            entity.Property(item => item.ParentDirectoryName1).HasColumnName("parent_directory_name1");
            entity.Property(item => item.Directory1).HasColumnName("directory1");
            entity.Property(item => item.Mode2).HasColumnName("mode2");
            entity.Property(item => item.ParentDirectoryName2).HasColumnName("parent_directory_name2");
            entity.Property(item => item.Directory2).HasColumnName("directory2");
            entity.Property(item => item.Mode3).HasColumnName("mode3");
            entity.Property(item => item.ParentDirectoryName3).HasColumnName("parent_directory_name3");
            entity.Property(item => item.Directory3).HasColumnName("directory3");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            // 同じ番組を二度手動で予約させない。数えかたが狂うだけで、得るものがない。
            entity.HasIndex(item => item.ProgramId)
                .IsUnique()
                .HasFilter("program_id IS NOT NULL")
                .HasDatabaseName("uq_manual_reserves_program");
            entity.HasIndex(item => item.StartAt).HasDatabaseName("ix_manual_reserves_start_at");
        });

        modelBuilder.Entity<ReserveEntity>(entity =>
        {
            entity.ToTable("reserves", table =>
                table.HasCheckConstraint("ck_reserves_time", "end_at > start_at"));
            entity.HasKey(item => item.Id).HasName("pk_reserves");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(item => item.Key).HasColumnName("key");
            entity.Property(item => item.Source).HasColumnName("source");
            entity.Property(item => item.RuleId).HasColumnName("rule_id");
            entity.Property(item => item.ProgramId).HasColumnName("program_id");
            entity.Property(item => item.ManualReserveId).HasColumnName("manual_reserve_id");
            entity.Property(item => item.ChannelId).HasColumnName("channel_id");
            entity.Property(item => item.ChannelType).HasColumnName("channel_type");
            entity.Property(item => item.StartAt).HasColumnName("start_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.EndAt).HasColumnName("end_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.HalfWidthName).HasColumnName("half_width_name");
            entity.Property(item => item.Priority).HasColumnName("priority").HasDefaultValue(0);
            entity.Property(item => item.IsSkip).HasColumnName("is_skip");
            entity.Property(item => item.IsConflict).HasColumnName("is_conflict");
            entity.Property(item => item.IsOverlap).HasColumnName("is_overlap");
            entity.Property(item => item.TunerIndex).HasColumnName("tuner_index");
            entity.Property(item => item.GeneratedAt).HasColumnName("generated_at").HasColumnType("timestamp with time zone");
            entity.HasIndex(item => item.Key).IsUnique().HasDatabaseName("uq_reserves_key");
            entity.HasIndex(item => item.StartAt).HasDatabaseName("ix_reserves_start_at");
            entity.HasIndex(item => item.RuleId).HasDatabaseName("ix_reserves_rule");
            entity.HasOne(item => item.ManualReserve)
                .WithMany()
                .HasForeignKey(item => item.ManualReserveId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_reserves_manual_reserve");
        });

        modelBuilder.Entity<ReserveSkipEntity>(entity =>
        {
            entity.ToTable("reserve_skips");
            entity.HasKey(item => item.Key).HasName("pk_reserve_skips");
            entity.Property(item => item.Key).HasColumnName("key");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<RecordedEntity>(entity =>
        {
            entity.ToTable("recorded", table =>
                table.HasCheckConstraint("ck_recorded_time", "end_at >= start_at"));
            entity.HasKey(item => item.Id).HasName("pk_recorded");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(item => item.ProgramId).HasColumnName("program_id");
            entity.Property(item => item.RuleId).HasColumnName("rule_id");
            entity.Property(item => item.ChannelId).HasColumnName("channel_id");
            entity.Property(item => item.StartAt).HasColumnName("start_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.EndAt).HasColumnName("end_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.HalfWidthName).HasColumnName("half_width_name");
            entity.Property(item => item.Description).HasColumnName("description");
            entity.Property(item => item.HalfWidthDescription).HasColumnName("half_width_description");
            entity.Property(item => item.Extended).HasColumnName("extended");
            entity.Property(item => item.HalfWidthExtended).HasColumnName("half_width_extended");
            entity.Property(item => item.Genre1).HasColumnName("genre1");
            entity.Property(item => item.SubGenre1).HasColumnName("sub_genre1");
            entity.Property(item => item.Genre2).HasColumnName("genre2");
            entity.Property(item => item.SubGenre2).HasColumnName("sub_genre2");
            entity.Property(item => item.Genre3).HasColumnName("genre3");
            entity.Property(item => item.SubGenre3).HasColumnName("sub_genre3");
            entity.Property(item => item.IsRecording).HasColumnName("is_recording");
            entity.Property(item => item.IsProtected).HasColumnName("is_protected");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.HasIndex(item => item.StartAt).HasDatabaseName("ix_recorded_start_at");
            entity.HasIndex(item => item.IsRecording).HasDatabaseName("ix_recorded_is_recording");
            entity.HasIndex(item => item.ChannelId).HasDatabaseName("ix_recorded_channel");
            entity.HasIndex(item => item.RuleId).HasDatabaseName("ix_recorded_rule");
            // 同じ予約で二重に録らない。再起動直後の取りこぼしを直す処理が二度動いても増えない。
            entity.HasIndex(item => item.ProgramId)
                .IsUnique()
                .HasFilter("program_id IS NOT NULL")
                .HasDatabaseName("uq_recorded_program");
        });

        modelBuilder.Entity<VideoFileEntity>(entity =>
        {
            entity.ToTable("video_files", table =>
                table.HasCheckConstraint("ck_video_files_type", "type IN ('ts', 'encoded')"));
            entity.HasKey(item => item.Id).HasName("pk_video_files");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(item => item.RecordedId).HasColumnName("recorded_id");
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.Filename).HasColumnName("filename");
            entity.Property(item => item.ParentDirectoryName).HasColumnName("parent_directory_name");
            entity.Property(item => item.Type).HasColumnName("type");
            entity.Property(item => item.Size).HasColumnName("size");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.HasIndex(item => item.RecordedId).HasDatabaseName("ix_video_files_recorded");
            entity.HasOne(item => item.Recorded)
                .WithMany(recorded => recorded.VideoFiles)
                .HasForeignKey(item => item.RecordedId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_video_files_recorded");
        });

        modelBuilder.Entity<RecordedTagEntity>(entity =>
        {
            entity.ToTable("recorded_tags");
            entity.HasKey(item => item.Id).HasName("pk_recorded_tags");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(item => item.Name).HasColumnName("name");
            entity.Property(item => item.Color).HasColumnName("color");
            entity.HasIndex(item => item.Name).IsUnique().HasDatabaseName("uq_recorded_tags_name");
        });

        modelBuilder.Entity<RecordedTagLinkEntity>(entity =>
        {
            entity.ToTable("recorded_tag_links");
            entity.HasKey(item => new { item.RecordedId, item.TagId }).HasName("pk_recorded_tag_links");
            entity.Property(item => item.RecordedId).HasColumnName("recorded_id");
            entity.Property(item => item.TagId).HasColumnName("tag_id");
            entity.HasIndex(item => item.TagId).HasDatabaseName("ix_recorded_tag_links_tag");
            entity.HasOne(item => item.Recorded)
                .WithMany(recorded => recorded.TagLinks)
                .HasForeignKey(item => item.RecordedId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_recorded_tag_links_recorded");
            entity.HasOne(item => item.Tag)
                .WithMany(tag => tag.Links)
                .HasForeignKey(item => item.TagId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_recorded_tag_links_tag");
        });

        modelBuilder.Entity<EpgSyncStateEntity>(entity =>
        {
            entity.ToTable("epg_sync_state", table =>
                table.HasCheckConstraint("ck_epg_sync_state_singleton", "singleton_id = 1"));
            entity.HasKey(item => item.SingletonId).HasName("pk_epg_sync_state");
            entity.Property(item => item.SingletonId).HasColumnName("singleton_id").ValueGeneratedNever();
            entity.Property(item => item.Generation).HasColumnName("generation").HasDefaultValue(0L);
            entity.Property(item => item.NeedsFullSync).HasColumnName("needs_full_sync").HasDefaultValue(true);
            entity.Property(item => item.LastAttemptAt).HasColumnName("last_attempt_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastSuccessAt).HasColumnName("last_success_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastStreamEventAt).HasColumnName("last_stream_event_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastError).HasColumnName("last_error");
        });
    }
}
