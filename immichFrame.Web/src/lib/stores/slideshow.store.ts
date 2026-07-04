import { writable } from 'svelte/store';

function createSlideshowStore() {
  const instantTransition = writable<boolean>(false);

  return {
    instantTransition,
  };
}

export const slideshowStore = createSlideshowStore();
