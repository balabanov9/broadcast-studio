FROM ubuntu:24.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends software-properties-common ca-certificates xvfb libgl1-mesa-dri mesa-utils fonts-dejavu-core && add-apt-repository -y ppa:casparcg/ppa && apt-get update && apt-get install -y --no-install-recommends casparcg-server casparcg-scanner && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home --uid 10001 broadcast && mkdir -p /media /templates /opt/casparcg/log /opt/casparcg/data && chown -R broadcast:broadcast /opt/casparcg
WORKDIR /opt/casparcg
COPY casparcg.config ./casparcg.config
USER broadcast
ENV LIBGL_ALWAYS_SOFTWARE=1
EXPOSE 5250
ENTRYPOINT ["xvfb-run", "-a", "-s", "-screen 0 1920x1080x24", "casparcg-server", "/opt/casparcg/casparcg.config"]
