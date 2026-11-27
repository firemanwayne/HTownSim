FROM ubuntu:24.04

RUN apt-get update \
    && apt-get install -y --no-install-recommends libatomic1 libgcc-s1 libstdc++6 libgtk-3-0t64 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/htownsim
COPY . .
RUN chmod +x HTownSimServer.x86_64

ENV HOME=/tmp
USER 10001:10001
ENTRYPOINT ["./HTownSimServer.x86_64", "-batchmode", "-nographics", "-city-server", "-logFile", "-"]
