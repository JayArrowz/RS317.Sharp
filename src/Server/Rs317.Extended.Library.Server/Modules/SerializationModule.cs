﻿using System;
using System.Collections.Generic;
using System.Text;
using Autofac;
using FreecraftCore.Serializer;
using GladNet;
using JetBrains.Annotations;

namespace Rs317.Extended
{
	public sealed class SerializationModule : Module
	{
		protected override void Load([NotNull] ContainerBuilder builder)
		{
			if(builder == null) throw new ArgumentNullException(nameof(builder));

			builder.RegisterType<SerializerService>()
				.As<ISerializerService>()
				.OnActivated(args =>
				{
					RegisterPayloads(args.Instance);
				})
				.SingleInstance();

			builder.RegisterType<FreecraftCoreGladNetSerializerAdapter>()
				.As<INetworkSerializationService>();
		}

		private void RegisterPayloads([NotNull] SerializerService serializer)
		{
			if(serializer == null) throw new ArgumentNullException(nameof(serializer));

			serializer.RegisterType<BaseGameClientPayload>();
			serializer.RegisterType<BaseGameServerPayload>();
			serializer.RegisterType<UnknownClientGamePayload>();
			serializer.RegisterType<UnknownServerGamePayload>();

			foreach(var payload in GameServerMetadataMarker.PayloadTypes)
			{
				Console.WriteLine($"Registering: {payload.Name}");
				serializer.RegisterType(payload);
			}

			serializer.Compile();
		}
	}
}
