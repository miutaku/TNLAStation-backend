using Microsoft.EntityFrameworkCore;

namespace TNLAStation.Infrastructure.Persistence;

public sealed class EpgDbContext(DbContextOptions<EpgDbContext> options) : DbContext(options)
{
    public DbSet<EpgChannelEntity> Channels => Set<EpgChannelEntity>();

    public DbSet<EpgProgramEntity> Programs => Set<EpgProgramEntity>();

    public DbSet<EpgSyncStateEntity> SyncStates => Set<EpgSyncStateEntity>();

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
