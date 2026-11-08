#include "GLP_player.h"

#include <stdlib.h>

typedef struct GLP_AudioPlayerImpl
{

} GLP_AudioPlayerImpl;

GLP_Result GLP_CreateAudioPlayer(const GLP_AudioPlayerInfo *info, GLP_AudioPlayer **player)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) malloc(sizeof(GLP_AudioPlayerImpl));

    *player = (GLP_AudioPlayer *) impl;
    return GLP_RESULT_OK;
}

GLP_Result GLP_DestroyAudioPlayer(GLP_AudioPlayer *player)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) player;
    free(impl);

    return GLP_RESULT_OK;
}
