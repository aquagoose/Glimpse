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
    if (argc < 2)
    {
        printf("Please enter a filename!");
        return 2;
    }

    GLP_AudioPlayerInfo playerInfo =
    {
        .sampleRate = 48000
    };

    GLP_AudioPlayer *player;
    GLP_CHECK(GLP_CreateAudioPlayer(&playerInfo, &player), "Create audio player");

    GLP_CHECK(GLP_PlayTrack(player, argv[1]), "Play track");

    while (GLP_GetPlaybackState(player) == GLP_PLAYBACK_STATE_PLAYING)
    {
        sleep(1);
    }

    GLP_CHECK(GLP_DestroyAudioPlayer(player), "Destroy audio player");
    return 0;
}