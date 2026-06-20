<script lang="ts">
	import { onMount } from 'svelte';
	import {
		listLinks,
		createLink,
		updateLink,
		deleteLink,
		listAccounts,
		browseAccount,
		type Link,
		type Account,
		type BrowseResult
	} from '$lib/adminApi';

	let links = $state<Link[]>([]);
	let accounts = $state<Account[]>([]);
	let loading = $state(true);
	let error = $state('');

	let editing = $state<Link | null>(null);
	let isNew = $state(false);
	let saving = $state(false);
	let saveError = $state('');

	let draft = $state<Partial<Link>>({});
	let albumsText = $state('');
	let excludedAlbumsText = $state('');
	let peopleText = $state('');
	let tagsText = $state('');

	let browse = $state<BrowseResult | null>(null);
	let browsing = $state(false);
	let browseError = $state('');

	const inputClass =
		'w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400';

	let origin = $state('');
	onMount(() => {
		origin = window.location.origin;
		load();
	});

	async function load() {
		loading = true;
		error = '';
		try {
			[links, accounts] = await Promise.all([listLinks(), listAccounts()]);
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to load links.';
		} finally {
			loading = false;
		}
	}

	function startAdd() {
		draft = {
			slug: '',
			name: '',
			accountId: accounts[0]?.id ?? '',
			accessPolicy: 'None',
			pin: '',
			enabled: true,
			showMemories: false,
			showFavorites: false,
			showArchived: false,
			showVideos: false,
			imagesFromDays: null,
			imagesFromDate: null,
			imagesUntilDate: null,
			rating: null
		};
		albumsText = excludedAlbumsText = peopleText = tagsText = '';
		browse = null;
		browseError = '';
		saveError = '';
		isNew = true;
		editing = {} as Link;
	}

	function startEdit(link: Link) {
		draft = { ...link, pin: '' };
		albumsText = link.albums.join('\n');
		excludedAlbumsText = link.excludedAlbums.join('\n');
		peopleText = link.people.join('\n');
		tagsText = link.tags.join('\n');
		browse = null;
		browseError = '';
		saveError = '';
		isNew = false;
		editing = link;
	}

	function cancel() {
		editing = null;
	}

	function lines(text: string): string[] {
		return text.split('\n').map((s) => s.trim()).filter((s) => s.length > 0);
	}
	function nullableDate(v: unknown): string | null {
		return v ? String(v) : null;
	}

	async function save() {
		saving = true;
		saveError = '';
		try {
			const payload: Partial<Link> = {
				...draft,
				imagesFromDate: nullableDate(draft.imagesFromDate),
				imagesUntilDate: nullableDate(draft.imagesUntilDate),
				albums: lines(albumsText),
				excludedAlbums: lines(excludedAlbumsText),
				people: lines(peopleText),
				tags: lines(tagsText)
			};
			if (isNew) await createLink(payload);
			else await updateLink((editing as Link).id, payload);
			editing = null;
			await load();
		} catch (e) {
			saveError = e instanceof Error ? e.message : 'Failed to save link.';
		} finally {
			saving = false;
		}
	}

	async function remove(link: Link) {
		if (!confirm(`Delete the link /${link.slug}?`)) return;
		try {
			await deleteLink(link.id);
			await load();
		} catch (e) {
			error = e instanceof Error ? e.message : 'Failed to delete link.';
		}
	}

	async function loadFromServer() {
		browseError = '';
		const account = accounts.find((a) => a.id === draft.accountId);
		if (!account) {
			browseError = 'Choose an account first.';
			return;
		}
		browsing = true;
		try {
			browse = await browseAccount({ immichServerUrl: account.immichServerUrl, accountId: account.id });
		} catch (e) {
			browse = null;
			browseError = e instanceof Error ? e.message : 'Could not reach the server.';
		} finally {
			browsing = false;
		}
	}

	function isChecked(text: string, id: string): boolean {
		return lines(text).includes(id);
	}
	function toggleId(text: string, id: string, checked: boolean): string {
		const set = new Set(lines(text));
		if (checked) set.add(id);
		else set.delete(id);
		return [...set].join('\n');
	}

	function accountLabel(id: string): string {
		return accounts.find((a) => a.id === id)?.immichServerUrl ?? 'unknown account';
	}
</script>

{#if editing}
	<section class="rounded-xl border border-slate-700 bg-slate-800/60 p-5">
		<h2 class="mb-4 text-sm font-semibold uppercase tracking-wide text-slate-400">
			{isNew ? 'New link' : 'Edit link'}
		</h2>
		<div class="space-y-4">
			<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
				<label class="block text-sm">
					<span class="mb-1 block text-slate-300">Link path (slug)</span>
					<input bind:value={draft.slug} placeholder="vacation" class={inputClass} />
					<span class="mt-1 block truncate text-xs text-slate-500">{origin}/{(draft.slug ?? '').toLowerCase().replace(/[^a-z0-9-]+/g, '-').replace(/^-+|-+$/g, '') || '…'}</span>
				</label>
				<label class="block text-sm">
					<span class="mb-1 block text-slate-300">Name</span>
					<input bind:value={draft.name} placeholder="Vacation photos" class={inputClass} />
				</label>
			</div>

			<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
				<label class="block text-sm">
					<span class="mb-1 block text-slate-300">Account</span>
					<select bind:value={draft.accountId} class={inputClass}>
						{#each accounts as a}<option value={a.id}>{a.immichServerUrl}</option>{/each}
					</select>
				</label>
				<div class="grid grid-cols-2 gap-3">
					<label class="block text-sm">
						<span class="mb-1 block text-slate-300">Access</span>
						<select bind:value={draft.accessPolicy} class={inputClass}>
							<option value="None">Public</option>
							<option value="Pin">PIN</option>
							<option value="ViewerAuth">Viewer login</option>
						</select>
					</label>
					{#if draft.accessPolicy === 'Pin'}
						<label class="block text-sm">
							<span class="mb-1 block text-slate-300">PIN {isNew ? '' : '(blank = keep)'}</span>
							<input bind:value={draft.pin} inputmode="numeric" autocomplete="off" class={inputClass} />
						</label>
					{/if}
				</div>
			</div>

			<label class="flex items-center gap-2 text-sm">
				<input type="checkbox" bind:checked={draft.enabled} class="h-4 w-4" /> Enabled
			</label>

			<div>
				<button type="button" onclick={loadFromServer} disabled={browsing} class="rounded-md border border-slate-600 px-3 py-1.5 text-sm hover:bg-slate-700 disabled:opacity-50">
					{browsing ? 'Loading…' : 'Load albums & people from account'}
				</button>
				{#if browseError}<span class="ml-3 text-sm text-red-400">{browseError}</span>{/if}
				{#if browse && browse.warnings.length}
					<ul class="mt-2 space-y-1 text-xs text-amber-300">
						{#each browse.warnings as w}<li>⚠ {w}</li>{/each}
					</ul>
				{/if}
			</div>

			<div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
				<label class="flex items-center gap-2 text-sm"><input type="checkbox" bind:checked={draft.showFavorites} class="h-4 w-4" /> Favorites</label>
				<label class="flex items-center gap-2 text-sm"><input type="checkbox" bind:checked={draft.showMemories} class="h-4 w-4" /> Memories</label>
				<label class="flex items-center gap-2 text-sm"><input type="checkbox" bind:checked={draft.showArchived} class="h-4 w-4" /> Archived</label>
				<label class="flex items-center gap-2 text-sm"><input type="checkbox" bind:checked={draft.showVideos} class="h-4 w-4" /> Videos</label>
			</div>

			<div>
				<span class="mb-1 block text-sm text-slate-300">Albums</span>
				{#if browse}
					<div class="mb-2 max-h-36 overflow-y-auto rounded-md border border-slate-600 bg-slate-700/40 p-2">
						{#each browse.albums as a (a.id)}
							<label class="flex items-center gap-2 py-0.5 text-sm"><input type="checkbox" checked={isChecked(albumsText, a.id)} onchange={(e) => (albumsText = toggleId(albumsText, a.id, e.currentTarget.checked))} /><span class="truncate">{a.name}</span></label>
						{:else}
							<p class="text-xs text-slate-500">No albums.</p>
						{/each}
					</div>
				{/if}
				<textarea bind:value={albumsText} rows="2" placeholder="Album IDs, one per line" class="{inputClass} font-mono text-xs"></textarea>
			</div>

			<div>
				<span class="mb-1 block text-sm text-slate-300">People</span>
				{#if browse}
					<div class="mb-2 max-h-36 overflow-y-auto rounded-md border border-slate-600 bg-slate-700/40 p-2">
						{#each browse.people as p (p.id)}
							<label class="flex items-center gap-2 py-0.5 text-sm"><input type="checkbox" checked={isChecked(peopleText, p.id)} onchange={(e) => (peopleText = toggleId(peopleText, p.id, e.currentTarget.checked))} /><span class="truncate">{p.name}</span></label>
						{:else}
							<p class="text-xs text-slate-500">No named people.</p>
						{/each}
					</div>
				{/if}
				<textarea bind:value={peopleText} rows="2" placeholder="Person IDs, one per line" class="{inputClass} font-mono text-xs"></textarea>
			</div>

			<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
				<label class="block text-sm"><span class="mb-1 block text-slate-300">Tags (one per line)</span><textarea bind:value={tagsText} rows="2" class="{inputClass} font-mono text-xs"></textarea></label>
				<label class="block text-sm"><span class="mb-1 block text-slate-300">Excluded album IDs</span><textarea bind:value={excludedAlbumsText} rows="2" class="{inputClass} font-mono text-xs"></textarea></label>
			</div>

			{#if saveError}<p class="text-sm text-red-400">{saveError}</p>{/if}

			<div class="flex gap-3">
				<button onclick={save} disabled={saving} class="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium hover:bg-indigo-500 disabled:opacity-50">{saving ? 'Saving…' : 'Save link'}</button>
				<button onclick={cancel} class="rounded-md border border-slate-600 px-4 py-2 text-sm hover:bg-slate-800">Cancel</button>
			</div>
		</div>
	</section>
{:else}
	<div class="space-y-4">
		<div class="flex items-center justify-between">
			<p class="text-sm text-slate-400">Shareable slideshow links, each scoped to its own photos.</p>
			<button onclick={startAdd} disabled={accounts.length === 0} class="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium hover:bg-indigo-500 disabled:opacity-50">Add link</button>
		</div>

		{#if error}<p class="rounded-md border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-300">{error}</p>{/if}

		{#if loading}
			<p class="text-slate-400">Loading…</p>
		{:else if accounts.length === 0}
			<div class="rounded-xl border border-dashed border-slate-700 p-8 text-center text-slate-400">Add an account first, then create links.</div>
		{:else if links.length === 0}
			<div class="rounded-xl border border-dashed border-slate-700 p-8 text-center text-slate-400">No links yet. Add one to share a slideshow.</div>
		{:else}
			{#each links as link (link.id)}
				<div class="flex items-center justify-between rounded-xl border border-slate-700 bg-slate-800/60 p-4">
					<div class="min-w-0">
						<div class="flex items-center gap-2">
							<span class="font-medium">{link.name}</span>
							{#if !link.enabled}<span class="rounded bg-slate-600 px-1.5 py-0.5 text-xs">disabled</span>{/if}
							{#if link.accessPolicy === 'Pin'}
								<span class="rounded bg-amber-500/20 px-1.5 py-0.5 text-xs text-amber-300">PIN</span>
							{:else if link.accessPolicy === 'ViewerAuth'}
								<span class="rounded bg-sky-500/20 px-1.5 py-0.5 text-xs text-sky-300">login</span>
							{:else}
								<span class="rounded bg-green-500/20 px-1.5 py-0.5 text-xs text-green-300">public</span>
							{/if}
						</div>
						<a href="/{link.slug}" target="_blank" rel="noreferrer" class="mt-0.5 block truncate text-sm text-indigo-300 hover:underline">{origin}/{link.slug}</a>
						<p class="mt-0.5 truncate text-xs text-slate-500">{accountLabel(link.accountId)}</p>
					</div>
					<div class="flex shrink-0 gap-2">
						<button onclick={() => startEdit(link)} class="rounded-md border border-slate-600 px-3 py-1.5 text-sm hover:bg-slate-700">Edit</button>
						<button onclick={() => remove(link)} class="rounded-md border border-red-500/40 px-3 py-1.5 text-sm text-red-300 hover:bg-red-500/10">Delete</button>
					</div>
				</div>
			{/each}
		{/if}
	</div>
{/if}
