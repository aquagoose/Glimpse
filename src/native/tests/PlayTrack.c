#include "GLP/audio/GLP_player.h"

#include <stdio.h>
#include <unistd.h>

#define GLP_CHECK(result, operation) {\
    printf(operation"\n");\
    GLP_Result res = result;\
    if (res != GLP_RESULT_OK)\
    {\
        printf("Operation '"operation"' failed: %d\n", res);\
        return 1;\
    }\
}

int main(int argc, char **argv)
{
    GLP_AudioPlayerInfo playerInfo =
    {
        .sampleRate = 48000
    };

    GLP_AudioPlayer *player;
    GLP_CHECK(GLP_CreateAudioPlayer(&playerInfo, &player), "Create audio player");

    GLP_CHECK(GLP_PlayTrack(player, "/Users/aqua/Music/Ernest Gold - Fight for Survival [1EMV923r7cg].mp3"), "Play track");

    sleep(2);

    GLP_CHECK(GLP_DestroyAudioPlayer(player), "Destroy audio player");
    return 0;
}